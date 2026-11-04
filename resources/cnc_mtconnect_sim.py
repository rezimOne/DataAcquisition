#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
cnc_mtconnect_sim.py - symulator 5-osiowego centrum obrobkowego
                       (klasa Mazak VARIAXIS / INTEGREX: X, Y, Z + B, C)
                       wraz z emulatorem agenta MTConnect.

JEDEN PLIK, tylko biblioteka standardowa. Windows 11, py 3.9+.

================================ URUCHOMIENIE ================================

  py cnc_mtconnect_sim.py                       # agent HTTP na http://localhost:5000
  py cnc_mtconnect_sim.py --host 0.0.0.0        # dostep z LAN
  py cnc_mtconnect_sim.py --port 5000 --hz 10   # 10 Hz
  py cnc_mtconnect_sim.py --console             # + podglad w konsoli
  py cnc_mtconnect_sim.py --csv dane.csv --jsonl dane.jsonl
  py cnc_mtconnect_sim.py --shdr 7878           # + surowy adapter SHDR (TCP)
  py cnc_mtconnect_sim.py --no-agent --console  # sam symulator, bez HTTP
  py cnc_mtconnect_sim.py --seed 42             # powtarzalny przebieg

Zatrzymanie: Ctrl+C.

============================= ENDPOINTY AGENTA ===============================

  GET /probe                    MTConnectDevices - model urzadzenia i dataItemy
  GET /current                  MTConnectStreams - migawka ostatnich wartosci
  GET /current?at=N             migawka na sekwencji N
  GET /sample?from=N&count=M    okno z bufora kolowego
  GET /sample?interval=200      strumien multipart/x-mixed-replace
  GET /                         to samo co /probe

Przeplyw dla workera:
  1. GET /probe                      -> poznaj dataItemId
  2. GET /current                    -> zapamietaj Header/@nextSequence i @instanceId
  3. GET /sample?from=<nextSequence> -> w kolko, z kazdym nowym nextSequence
  4. zmiana Header/@instanceId       -> agent restartowal, wroc do /current

UWAGI
  - Obserwacje w dokumencie sa grupowane po komponentach, wiec kolejnosc w XML
    NIE jest kolejnoscia sekwencji. Do synchronizacji sluzy wylacznie
    Header/@nextSequence, nie pozycja elementu.
  - SAMPLE (pozycje, obciazenia, temperatury, posuw, obroty) trafiaja do bufora
    co tick; EVENT i CONDITION tylko przy zmianie wartosci - jak w realnym
    agencie, wiec worker nie dostanie tysiaca identycznych Execution=ACTIVE.
  - PATH_FEEDRATE jest w MILLIMETER/SECOND (zgodnie ze specyfikacja);
    w CSV/JSONL/SHDR ten sam posuw jest w mm/min.
  - Kody alarmow i nazwa maszyny sa syntetyczne - prawdopodobne, ale nie
    przepisane z dokumentacji Mazatrol/Smooth. Podmien liste ALARMS, jesli
    parser po drugiej stronie oczekuje realnych kodow.
"""

import argparse
import csv
import json
import math
import random
import socket
import sys
import threading
import time
from collections import deque
from datetime import datetime, timezone
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from urllib.parse import urlparse, parse_qs
from xml.sax.saxutils import escape, quoteattr

# =============================== MODEL MASZYNY ===============================

AXES = ("X", "Y", "Z", "B", "C")
LINEAR = ("X", "Y", "Z")

# (kod, tekst, czy wstrzymuje posuw)
ALARMS = [
    ("AL-0123", "SPINDLE OVERLOAD", True),
    ("AL-0217", "B-AXIS SERVO TEMPERATURE HIGH", False),
    ("AL-0304", "COOLANT LEVEL LOW", False),
    ("AL-0411", "TOOL LIFE EXPIRED", False),
    ("AL-0502", "C-AXIS CLAMP PRESSURE DROP", True),
]


def utcnow():
    return datetime.now(timezone.utc)


def iso(ts):
    return ts.strftime("%Y-%m-%dT%H:%M:%S.") + f"{ts.microsecond // 1000:03d}Z"


class Machine:
    """Uproszczony model kinematyki i obciazen 5-osiowego centrum."""

    def __init__(self, seed=None):
        self.rng = random.Random(seed)
        self.t = 0.0                       # czas kalendarzowy (zdarzenia)
        self.tp = 0.0                      # czas programu (rosnie tylko przy ruchu)
        self.pos = {"X": 0.0, "Y": 94.0, "Z": -50.0, "B": 0.0, "C": 0.0}
        self.prev_pos = dict(self.pos)
        self._warm = False
        self.execution = "ACTIVE"          # ACTIVE | FEED_HOLD | READY | INTERRUPTED
        self.mode = "AUTOMATIC"            # AUTOMATIC | MANUAL | MDI
        self.program = "O1234.MAIN_IMPELLER"
        self.block = 1000
        self.tool = 1
        self.spindle_cmd = 8000.0
        self.spindle_act = 0.0
        self.spindle_load = 0.0
        self.spindle_temp = 28.0
        self.servo_temp = {a: 30.0 + self.rng.uniform(-1, 1) for a in AXES}
        self.axis_load = {a: 0.0 for a in AXES}
        self.feed_override = 100
        self.coolant = "ON"
        self.part_count = 0
        self.path_feedrate = 0.0           # wypadkowy posuw [mm/min]
        self.alarm = None                  # (kod, tekst)
        self._hold_until = 0.0
        self._tc_until = 0.0
        self._alarm_until = 0.0
        self._cycle_len = 95.0             # dlugosc cyklu detalu [s]

    # trajektoria: splot ruchu liniowego i obrotowego (obrobka lopatki wirnika)
    def _target(self, t):
        return {
            "X": 120.0 * math.sin(0.25 * t) + 6.0 * math.sin(2.10 * t),
            "Y": 90.0 * math.cos(0.17 * t) + 4.0 * math.cos(1.70 * t),
            "Z": -50.0 + 12.0 * math.sin(0.60 * t) + 3.0 * math.sin(0.031 * t),
            "B": 95.0 * math.sin(0.08 * t),          # przechyl stolu -95..95 st
            "C": (14.0 * t) % 360.0,                  # obrot ciagly
        }

    def _events(self, dt):
        now = self.t

        if self.alarm and now >= self._alarm_until:
            self.alarm = None
        if self.execution == "FEED_HOLD" and now >= self._hold_until and not self.alarm:
            self.execution = "ACTIVE"
        if self.execution == "READY" and now >= self._tc_until:
            self.execution = "ACTIVE"

        # wymiana narzedzia co ~25-40 s obrobki
        if self.execution == "ACTIVE" and self.rng.random() < dt / self.rng.uniform(25, 40):
            self.tool = 1 + (self.tool % 8)
            self.spindle_cmd = self.rng.choice([4500, 6000, 8000, 10500, 12000])
            self.execution = "READY"
            self._tc_until = now + self.rng.uniform(3.0, 6.0)

        # sporadyczne wstrzymanie posuwu przez operatora
        if self.execution == "ACTIVE" and self.rng.random() < dt / 180.0:
            self.execution = "FEED_HOLD"
            self._hold_until = now + self.rng.uniform(2.0, 8.0)

        # alarm
        if not self.alarm and self.rng.random() < dt / 240.0:
            code, text, stops = self.rng.choice(ALARMS)
            self.alarm = (code, text)
            self._alarm_until = now + self.rng.uniform(5.0, 15.0)
            if stops:
                self.execution = "INTERRUPTED"
                self._hold_until = self._alarm_until
        if self.execution == "INTERRUPTED" and not self.alarm:
            self.execution = "ACTIVE"

        if int((now - dt) / self._cycle_len) != int(now / self._cycle_len):
            self.part_count += 1

        if self.rng.random() < dt / 30.0:
            self.feed_override = self.rng.choice([80, 90, 100, 100, 100, 110, 120])

    def step(self, dt):
        if not self._warm:                 # bez skoku pozycji w pierwszej probce
            self.pos = self._target(0.0)
            self.prev_pos = dict(self.pos)
            self._warm = True
        self.t += dt
        self._events(dt)
        moving = self.execution == "ACTIVE"

        self.prev_pos = dict(self.pos)
        if moving:
            self.tp += dt                  # czas programu stoi przy FEED_HOLD/READY,
            tgt = self._target(self.tp)    # wiec po wznowieniu nie ma skoku pozycji
            for a in AXES:
                # drobny blad nadazania serwa + szum enkodera
                self.pos[a] = tgt[a] + self.rng.gauss(0.0, 0.0015)

        # posuw wypadkowy z faktycznego przemieszczenia osi liniowych
        d = math.sqrt(sum((self.pos[a] - self.prev_pos[a]) ** 2 for a in LINEAR))
        feed = (d / dt) * 60.0 * (self.feed_override / 100.0) if moving else 0.0
        self.path_feedrate = min(feed, 42000.0)        # limit posuwu szybkiego

        # wrzeciono: rampa do zadanych obrotow
        target_rpm = self.spindle_cmd if moving else 0.0
        self.spindle_act += (target_rpm - self.spindle_act) * min(1.0, dt * 1.6)
        if self.spindle_act < 1.0:
            self.spindle_act = 0.0

        # obciazenie wrzeciona skorelowane z posuwem i glebokoscia skrawania
        depth = max(0.0, -self.pos["Z"]) / 60.0
        base = (8.0 + 0.006 * self.path_feedrate + 22.0 * depth) if self.spindle_act > 0 else 0.0
        if self.alarm and self.alarm[0] == "AL-0123":
            base += 45.0
        self.spindle_load = max(0.0, min(130.0, base + self.rng.gauss(0, 2.0)))

        # obciazenie i temperatura osi
        for a in AXES:
            v = abs(self.pos[a] - self.prev_pos[a]) / dt
            self.axis_load[a] = max(0.0, min(100.0, v * (0.9 if a in LINEAR else 2.2)
                                             + self.rng.gauss(0, 1.5)))
            drift = (0.02 * self.axis_load[a] - 0.35 * (self.servo_temp[a] - 30.0)) * dt
            self.servo_temp[a] += drift + self.rng.gauss(0, 0.01)

        self.spindle_temp += (0.012 * self.spindle_load
                              - 0.25 * (self.spindle_temp - 28.0)) * dt
        self.coolant = "ON" if self.spindle_act > 100 else "OFF"
        if moving:
            self.block += self.rng.choice([1, 1, 1, 2, 5])

    def sample(self):
        rec = {
            "timestamp": iso(utcnow()),
            "machine": "MAZAK-VARIAXIS-I700-SIM",
            "availability": "AVAILABLE",
            "execution": self.execution,
            "controller_mode": self.mode,
            "program": self.program,
            "block": f"N{self.block}",
            "tool_id": self.tool,
            "spindle_rpm_cmd": round(self.spindle_cmd, 0),
            "spindle_rpm_act": round(self.spindle_act, 1),
            "spindle_load_pct": round(self.spindle_load, 1),
            "spindle_temp_c": round(self.spindle_temp, 2),
            "path_feedrate_mm_min": round(self.path_feedrate, 1),
            "feed_override_pct": self.feed_override,
            "coolant": self.coolant,
            "part_count": self.part_count,
            "alarm_code": self.alarm[0] if self.alarm else "",
            "alarm_text": self.alarm[1] if self.alarm else "",
        }
        for a in AXES:
            unit = "mm" if a in LINEAR else "deg"
            rec[f"{a.lower()}_pos_{unit}"] = round(self.pos[a], 4)
            rec[f"{a.lower()}_load_pct"] = round(self.axis_load[a], 1)
            rec[f"{a.lower()}_temp_c"] = round(self.servo_temp[a], 2)
        return rec


# ============================= MODEL MTCONNECT ===============================

VERSION = "1.3"
NS_DEV = f"urn:mtconnect.org:MTConnectDevices:{VERSION}"
NS_STR = f"urn:mtconnect.org:MTConnectStreams:{VERSION}"
NS_ERR = f"urn:mtconnect.org:MTConnectError:{VERSION}"

DEVICE_NAME = "VMC5AX"
DEVICE_UUID = "MAZAK-SIM-001"
SENDER = "cnc-sim-agent"

# komponenty: key, element XML, id, name, rodzic
COMPONENTS = [
    ("dev",  None,         "d1",    DEVICE_NAME,  None),
    ("ctrl", "Controller", "ctrl",  "controller", "dev"),
    ("path", "Path",       "path1", "path",       "ctrl"),
    ("axes", "Axes",       "axes",  "base",       "dev"),
    ("X",    "Linear",     "x",     "X",          "axes"),
    ("Y",    "Linear",     "y",     "Y",          "axes"),
    ("Z",    "Linear",     "z",     "Z",          "axes"),
    ("B",    "Rotary",     "b",     "B",          "axes"),
    ("C",    "Rotary",     "c",     "C",          "axes"),
    ("S",    "Rotary",     "s",     "S",          "axes"),
]

# dataItemy: id, komponent, kategoria, typ, subType, jednostki, zrodlo w rec
DATA_ITEMS = [
    ("avail",  "dev",  "EVENT",     "AVAILABILITY",    None,     None,                "availability"),
    ("exec",   "path", "EVENT",     "EXECUTION",       None,     None,                "execution"),
    ("mode",   "path", "EVENT",     "CONTROLLER_MODE", None,     None,                "controller_mode"),
    ("pgm",    "path", "EVENT",     "PROGRAM",         None,     None,                "program"),
    ("block",  "path", "EVENT",     "BLOCK",           None,     None,                "block"),
    ("tool",   "path", "EVENT",     "TOOL_NUMBER",     None,     None,                "tool_id"),
    ("pcount", "path", "EVENT",     "PART_COUNT",      None,     None,                "part_count"),
    ("feed",   "path", "SAMPLE",    "PATH_FEEDRATE",   "ACTUAL", "MILLIMETER/SECOND",
     lambda r: round(r["path_feedrate_mm_min"] / 60.0, 3)),
    ("system", "ctrl", "CONDITION", "SYSTEM",          None,     None,                None),
    ("xpos",   "X",    "SAMPLE",    "POSITION",        "ACTUAL", "MILLIMETER",        "x_pos_mm"),
    ("xload",  "X",    "SAMPLE",    "LOAD",            None,     "PERCENT",           "x_load_pct"),
    ("xtemp",  "X",    "SAMPLE",    "TEMPERATURE",     None,     "CELSIUS",           "x_temp_c"),
    ("ypos",   "Y",    "SAMPLE",    "POSITION",        "ACTUAL", "MILLIMETER",        "y_pos_mm"),
    ("yload",  "Y",    "SAMPLE",    "LOAD",            None,     "PERCENT",           "y_load_pct"),
    ("ytemp",  "Y",    "SAMPLE",    "TEMPERATURE",     None,     "CELSIUS",           "y_temp_c"),
    ("zpos",   "Z",    "SAMPLE",    "POSITION",        "ACTUAL", "MILLIMETER",        "z_pos_mm"),
    ("zload",  "Z",    "SAMPLE",    "LOAD",            None,     "PERCENT",           "z_load_pct"),
    ("ztemp",  "Z",    "SAMPLE",    "TEMPERATURE",     None,     "CELSIUS",           "z_temp_c"),
    ("bang",   "B",    "SAMPLE",    "ANGLE",           "ACTUAL", "DEGREE",            "b_pos_deg"),
    ("bload",  "B",    "SAMPLE",    "LOAD",            None,     "PERCENT",           "b_load_pct"),
    ("btemp",  "B",    "SAMPLE",    "TEMPERATURE",     None,     "CELSIUS",           "b_temp_c"),
    ("cang",   "C",    "SAMPLE",    "ANGLE",           "ACTUAL", "DEGREE",            "c_pos_deg"),
    ("cload",  "C",    "SAMPLE",    "LOAD",            None,     "PERCENT",           "c_load_pct"),
    ("ctemp",  "C",    "SAMPLE",    "TEMPERATURE",     None,     "CELSIUS",           "c_temp_c"),
    ("srpm",   "S",    "SAMPLE",    "ROTARY_VELOCITY", "ACTUAL", "REVOLUTION/MINUTE", "spindle_rpm_act"),
    ("sload",  "S",    "SAMPLE",    "LOAD",            None,     "PERCENT",           "spindle_load_pct"),
    ("stemp",  "S",    "SAMPLE",    "TEMPERATURE",     None,     "CELSIUS",           "spindle_temp_c"),
]

DI_BY_ID = {d[0]: d for d in DATA_ITEMS}


def elem_name(type_):
    """POSITION -> Position, ROTARY_VELOCITY -> RotaryVelocity."""
    return "".join(p.capitalize() for p in type_.split("_"))


class Agent:
    """Bufor kolowy obserwacji z numeracja sekwencyjna - karmiony z petli glownej."""

    def __init__(self, buffer_size=131072):
        self.buffer_size = buffer_size
        self.instance_id = int(time.time())
        self.lock = threading.Lock()
        self.seq = 0
        self.buffer = deque(maxlen=buffer_size)
        self.latest = {}

    def _record(self, di_id, value, ts):
        self.seq += 1
        obs = {"seq": self.seq, "di": di_id, "ts": ts, "value": value}
        self.buffer.append(obs)
        self.latest[di_id] = obs

    def ingest(self, rec):
        ts = rec["timestamp"]
        with self.lock:
            for di_id, _comp, cat, _t, _st, _u, src in DATA_ITEMS:
                if cat == "CONDITION":
                    value = (("Fault", rec["alarm_code"], rec["alarm_text"])
                             if rec["alarm_code"] else ("Normal", "", ""))
                else:
                    value = src(rec) if callable(src) else rec[src]

                if cat == "SAMPLE":
                    self._record(di_id, value, ts)      # probka za kazdym razem
                else:
                    prev = self.latest.get(di_id)       # zdarzenie/stan: tylko zmiana
                    if prev is None or prev["value"] != value:
                        self._record(di_id, value, ts)

    def snapshot(self, at=None):
        with self.lock:
            if at is None:
                obs = list(self.latest.values())
            else:
                acc = {}
                for o in self.buffer:
                    if o["seq"] <= at:
                        acc[o["di"]] = o
                obs = list(acc.values())
            return obs, self.seq, self._first()

    def window(self, frm, count):
        with self.lock:
            first = self._first()
            if frm is None:
                frm = first
            if self.buffer and (frm < first or frm > self.seq + 1):
                return None, self.seq, first
            out = [o for o in self.buffer if o["seq"] >= frm][:count]
            return out, self.seq, first

    def _first(self):
        return self.buffer[0]["seq"] if self.buffer else 1


# ============================== GENEROWANIE XML ==============================

def _header(agent, tag, next_seq=None, first_seq=None, last_seq=None):
    a = [
        f"creationTime={quoteattr(iso(utcnow()))}",
        f"sender={quoteattr(SENDER)}",
        f'instanceId="{agent.instance_id}"',
        f"version={quoteattr(VERSION + '.0.0')}",
        f'bufferSize="{agent.buffer_size}"',
    ]
    if tag == "MTConnectDevices":
        a += ['assetBufferSize="1024"', 'assetCount="0"']
    elif tag == "MTConnectStreams":
        a += [f'nextSequence="{next_seq}"',
              f'firstSequence="{first_seq}"',
              f'lastSequence="{last_seq}"']
    return "  <Header " + " ".join(a) + "/>"


def _root_open(tag, ns):
    return (
        '<?xml version="1.0" encoding="UTF-8"?>\n'
        f"<{tag} xmlns={quoteattr(ns)} "
        'xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance" '
        f'xsi:schemaLocation={quoteattr(ns + " /schemas/" + tag + "_" + VERSION + ".xsd")}>'
    )


def probe_xml(agent):
    def children(parent_key, indent):
        kids = [c for c in COMPONENTS if c[4] == parent_key]
        if not kids:
            return ""
        out = [f"{indent}<Components>"]
        for key, elem, cid, name, _p in kids:
            out.append(f"{indent}  <{elem} id={quoteattr(cid)} name={quoteattr(name)}>")
            out.append(data_items(key, indent + "    "))
            out.append(children(key, indent + "    "))
            out.append(f"{indent}  </{elem}>")
        out.append(f"{indent}</Components>")
        return "\n".join(x for x in out if x)

    def data_items(comp_key, indent):
        items = [d for d in DATA_ITEMS if d[1] == comp_key]
        if not items:
            return ""
        out = [f"{indent}<DataItems>"]
        for di_id, _c, cat, type_, sub, units, _s in items:
            a = [f"category={quoteattr(cat)}", f"id={quoteattr(di_id)}",
                 f"type={quoteattr(type_)}"]
            if sub:
                a.append(f"subType={quoteattr(sub)}")
            if units:
                a.append(f"units={quoteattr(units)}")
            out.append(f"{indent}  <DataItem " + " ".join(a) + "/>")
        out.append(f"{indent}</DataItems>")
        return "\n".join(out)

    parts = [
        _root_open("MTConnectDevices", NS_DEV),
        _header(agent, "MTConnectDevices"),
        "  <Devices>",
        f'    <Device id="d1" name={quoteattr(DEVICE_NAME)} uuid={quoteattr(DEVICE_UUID)}>',
        '      <Description manufacturer="Mazak (SIM)" model="VARIAXIS i-700 (SIM)" '
        'serialNumber="SIM-0001">symulator, dane syntetyczne</Description>',
        data_items("dev", "      "),
        children("dev", "      "),
        "    </Device>",
        "  </Devices>",
        "</MTConnectDevices>",
    ]
    return "\n".join(p for p in parts if p)


def _observation_xml(o, indent):
    di_id, _c, cat, type_, sub, _u, _s = DI_BY_ID[o["di"]]
    base = (f"dataItemId={quoteattr(di_id)} timestamp={quoteattr(o['ts'])} "
            f'sequence="{o["seq"]}"')
    if cat == "CONDITION":
        state, code, text = o["value"]
        a = f"{base} type={quoteattr(type_)}"
        if code:
            a += f' nativeCode={quoteattr(code)} qualifier="" severity=""'
        return f"{indent}<{state} {a}>{escape(text)}</{state}>"
    a = base + (f" subType={quoteattr(sub)}" if sub else "")
    tag = elem_name(type_)
    return f"{indent}<{tag} {a}>{escape(str(o['value']))}</{tag}>"


def streams_xml(agent, observations, next_seq, first_seq, last_seq):
    by_comp = {}
    for o in observations:
        by_comp.setdefault(DI_BY_ID[o["di"]][1], []).append(o)

    body = []
    for key, elem, cid, name, _p in COMPONENTS:
        obs = by_comp.get(key)
        if not obs:
            continue
        body.append(f'      <ComponentStream component={quoteattr(elem or "Device")} '
                    f"name={quoteattr(name)} componentId={quoteattr(cid)}>")
        for cat, wrapper in (("SAMPLE", "Samples"), ("EVENT", "Events"),
                             ("CONDITION", "Condition")):
            group = [o for o in obs if DI_BY_ID[o["di"]][2] == cat]
            if not group:
                continue
            body.append(f"        <{wrapper}>")
            body += [_observation_xml(o, "          ") for o in group]
            body.append(f"        </{wrapper}>")
        body.append("      </ComponentStream>")

    parts = [
        _root_open("MTConnectStreams", NS_STR),
        _header(agent, "MTConnectStreams", next_seq, first_seq, last_seq),
        "  <Streams>",
        f'    <DeviceStream name={quoteattr(DEVICE_NAME)} uuid={quoteattr(DEVICE_UUID)}>',
        "\n".join(body),
        "    </DeviceStream>",
        "  </Streams>",
        "</MTConnectStreams>",
    ]
    return "\n".join(p for p in parts if p)


def error_xml(agent, code, text):
    return "\n".join([
        _root_open("MTConnectError", NS_ERR),
        _header(agent, "MTConnectError"),
        "  <Errors>",
        f"    <Error errorCode={quoteattr(code)}>{escape(text)}</Error>",
        "  </Errors>",
        "</MTConnectError>",
    ])


# ================================ SERWER HTTP ================================

BOUNDARY = "--------------------------mtconnect-sim"


class Handler(BaseHTTPRequestHandler):
    protocol_version = "HTTP/1.1"
    agent = None                      # wstrzykiwane w main()
    verbose = False

    def log_message(self, fmt, *args):
        if self.verbose:
            print(f"[HTTP] {self.address_string()} {fmt % args}", flush=True)

    def _send(self, body, status=200, ctype="text/xml; charset=UTF-8"):
        data = body.encode("utf-8")
        self.send_response(status)
        self.send_header("Content-Type", ctype)
        self.send_header("Content-Length", str(len(data)))
        self.end_headers()
        self.wfile.write(data)

    def _int(self, q, key, default=None):
        try:
            return int(q[key][0])
        except (KeyError, ValueError, IndexError):
            return default

    def do_GET(self):
        ag = self.agent
        u = urlparse(self.path)
        q = parse_qs(u.query)
        path = u.path.rstrip("/") or "/"

        if path in ("/", "/probe"):
            return self._send(probe_xml(ag))

        if path == "/current":
            at = self._int(q, "at")
            obs, last, first = ag.snapshot(at)
            obs.sort(key=lambda o: o["seq"])
            next_seq = (at + 1) if at is not None else last + 1
            return self._send(streams_xml(ag, obs, next_seq, first, last))

        if path == "/sample":
            interval = self._int(q, "interval")
            if interval is not None:
                return self._stream(max(0, interval), self._int(q, "from"),
                                    self._int(q, "count", 100))
            frm = self._int(q, "from")
            count = self._int(q, "count", 100)
            obs, last, first = ag.window(frm, count)
            if obs is None:
                return self._send(
                    error_xml(ag, "OUT_OF_RANGE",
                              f"'from' poza buforem ({first}..{last})"), status=404)
            next_seq = (obs[-1]["seq"] + 1) if obs else (frm or last + 1)
            return self._send(streams_xml(ag, obs, next_seq, first, last))

        self._send(error_xml(ag, "UNSUPPORTED", f"nieznana sciezka: {u.path}"), status=404)

    def _stream(self, interval_ms, frm, count):
        """multipart/x-mixed-replace - polaczenie zostaje otwarte."""
        ag = self.agent
        self.send_response(200)
        self.send_header("Content-Type", f"multipart/x-mixed-replace; boundary={BOUNDARY}")
        self.end_headers()
        nxt = frm if frm is not None else ag.seq + 1
        try:
            while True:
                obs, last, first = ag.window(nxt, count)
                if obs is None:                      # klient zostal w tyle za buforem
                    obs, last, first = ag.window(first, count)
                    nxt = first
                if obs:
                    nxt = obs[-1]["seq"] + 1
                    doc = streams_xml(ag, obs, nxt, first, last).encode("utf-8")
                    head = (f"--{BOUNDARY}\r\n"
                            f"Content-Type: text/xml\r\n"
                            f"Content-Length: {len(doc)}\r\n\r\n").encode("ascii")
                    self.wfile.write(head + doc + b"\r\n")
                    self.wfile.flush()
                time.sleep(max(0.01, interval_ms / 1000.0))
        except (BrokenPipeError, ConnectionResetError):
            pass                                     # worker sie rozlaczyl


# ============================== ADAPTER SHDR (TCP) ===========================

class ShdrServer(threading.Thread):
    """Surowy adapter SHDR: 'timestamp|klucz|wartosc|...'. Opcjonalny (--shdr)."""

    daemon = True

    def __init__(self, host, port):
        super().__init__()
        self.clients = []
        self.lock = threading.Lock()
        self.sock = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
        self.sock.setsockopt(socket.SOL_SOCKET, socket.SO_REUSEADDR, 1)
        self.sock.bind((host, port))
        self.sock.listen(5)

    def run(self):
        while True:
            try:
                conn, addr = self.sock.accept()
            except OSError:
                return
            with self.lock:
                self.clients.append(conn)
            print(f"[SHDR] polaczono: {addr[0]}:{addr[1]}", file=sys.stderr, flush=True)
            threading.Thread(target=self._heartbeat, args=(conn,), daemon=True).start()

    def _heartbeat(self, conn):
        """Agent MTConnect wysyla '* PING' i oczekuje '* PONG <ms>'."""
        try:
            buf = b""
            while True:
                data = conn.recv(1024)
                if not data:
                    return
                buf += data
                while b"\n" in buf:
                    line, buf = buf.split(b"\n", 1)
                    if line.strip().upper().startswith(b"* PING"):
                        conn.sendall(b"* PONG 10000\n")
        except OSError:
            return

    def send(self, rec):
        pairs = [
            ("avail", rec["availability"]),
            ("execution", rec["execution"]),
            ("mode", rec["controller_mode"]),
            ("program", rec["program"]),
            ("block", rec["block"]),
            ("tool_id", rec["tool_id"]),
            ("Sspeed", rec["spindle_rpm_act"]),
            ("Sload", rec["spindle_load_pct"]),
            ("Stemp", rec["spindle_temp_c"]),
            ("path_feedrate", rec["path_feedrate_mm_min"]),
            ("part_count", rec["part_count"]),
        ]
        for a in AXES:
            u = "mm" if a in LINEAR else "deg"
            pairs.append((f"{a}abs", rec[f"{a.lower()}_pos_{u}"]))
            pairs.append((f"{a}load", rec[f"{a.lower()}_load_pct"]))
        if rec["alarm_code"]:
            pairs.append(("system", f"fault|{rec['alarm_code']}|CRITICAL||{rec['alarm_text']}"))
        else:
            pairs.append(("system", "normal||||"))

        data = (rec["timestamp"] + "|"
                + "|".join(f"{k}|{v}" for k, v in pairs) + "\n").encode("utf-8")
        with self.lock:
            dead = []
            for c in self.clients:
                try:
                    c.sendall(data)
                except OSError:
                    dead.append(c)
            for c in dead:
                self.clients.remove(c)
                try:
                    c.close()
                except OSError:
                    pass


# ================================== GLOWNA ===================================

def main():
    p = argparse.ArgumentParser(
        description="Symulator 5-osiowego CNC + emulator agenta MTConnect")
    p.add_argument("--host", default="127.0.0.1", help="adres nasluchu (0.0.0.0 = LAN)")
    p.add_argument("--port", type=int, default=5000, help="port HTTP agenta (domyslnie 5000)")
    p.add_argument("--no-agent", action="store_true", help="nie uruchamiaj agenta HTTP")
    p.add_argument("--hz", type=float, default=5.0, help="czestotliwosc probkowania (domyslnie 5)")
    p.add_argument("--buffer", type=int, default=131072, help="rozmiar bufora kolowego agenta")
    p.add_argument("--duration", type=float, default=0.0, help="czas pracy w s (0 = bez konca)")
    p.add_argument("--console", action="store_true", help="podglad probek w konsoli")
    p.add_argument("--http-log", action="store_true", help="loguj zadania HTTP")
    p.add_argument("--csv", metavar="PLIK", help="zapis do CSV")
    p.add_argument("--jsonl", metavar="PLIK", help="zapis do JSON Lines")
    p.add_argument("--shdr", type=int, metavar="PORT", help="dodatkowo adapter SHDR na TCP")
    p.add_argument("--seed", type=int, help="ziarno losowosci (powtarzalny przebieg)")
    args = p.parse_args()

    dt = 1.0 / args.hz
    machine = Machine(seed=args.seed)
    agent = Agent(buffer_size=args.buffer)

    srv = None
    if not args.no_agent:
        Handler.agent = agent
        Handler.verbose = args.http_log
        srv = ThreadingHTTPServer((args.host, args.port), Handler)
        threading.Thread(target=srv.serve_forever, daemon=True).start()
        base = f"http://{'localhost' if args.host == '127.0.0.1' else args.host}:{args.port}"
        print(f"[AGENT] {base}/probe | {base}/current | {base}/sample?from=1", flush=True)
        print(f"[AGENT] instanceId={agent.instance_id} bufor={args.buffer} {args.hz} Hz",
              flush=True)

    shdr = None
    if args.shdr:
        shdr = ShdrServer(args.host, args.shdr)
        shdr.start()
        print(f"[SHDR]  nasluch na {args.host}:{args.shdr}", flush=True)

    csv_f = csv_w = jsonl_f = None
    try:
        if args.csv:
            csv_f = open(args.csv, "w", newline="", encoding="utf-8")
        if args.jsonl:
            jsonl_f = open(args.jsonl, "w", encoding="utf-8")

        t0 = time.perf_counter()
        n = 0
        while True:
            machine.step(dt)
            rec = machine.sample()
            n += 1

            agent.ingest(rec)
            if shdr is not None:
                shdr.send(rec)
            if csv_f is not None:
                if csv_w is None:
                    csv_w = csv.DictWriter(csv_f, fieldnames=list(rec.keys()), delimiter=";")
                    csv_w.writeheader()
                csv_w.writerow(rec)
            if jsonl_f is not None:
                jsonl_f.write(json.dumps(rec, ensure_ascii=False) + "\n")
            if args.console:
                al = f" !{rec['alarm_code']}" if rec["alarm_code"] else ""
                print(
                    f"{rec['timestamp']} {rec['execution']:<11} {rec['block']:>7} "
                    f"T{rec['tool_id']:02d} "
                    f"X{rec['x_pos_mm']:9.3f} Y{rec['y_pos_mm']:9.3f} Z{rec['z_pos_mm']:8.3f} "
                    f"B{rec['b_pos_deg']:8.3f} C{rec['c_pos_deg']:8.3f} "
                    f"S{rec['spindle_rpm_act']:8.1f} L{rec['spindle_load_pct']:5.1f}% "
                    f"F{rec['path_feedrate_mm_min']:8.1f}{al}",
                    flush=True,
                )

            if args.duration and (time.perf_counter() - t0) >= args.duration:
                break
            delay = t0 + n * dt - time.perf_counter()
            if delay > 0:
                time.sleep(delay)
            else:
                t0 = time.perf_counter() - n * dt    # nadrabianie po zacieciu
    except KeyboardInterrupt:
        print("\n[OK] zatrzymano", file=sys.stderr, flush=True)
    finally:
        for f in (csv_f, jsonl_f):
            if f is not None:
                f.close()
        if srv is not None:
            srv.shutdown()
            srv.server_close()


if __name__ == "__main__":
    main()
