using System.Globalization;
using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace DataAcquisition.Worker.MTConnect;

/// <summary>Parses MTConnect 1.3 documents (Devices, Streams, Error).</summary>
internal static class MTConnectParser
{
    private static readonly XNamespace DevicesNs = "urn:mtconnect.org:MTConnectDevices:1.3";
    private static readonly XNamespace StreamsNs = "urn:mtconnect.org:MTConnectStreams:1.3";
    private static readonly XNamespace ErrorNs = "urn:mtconnect.org:MTConnectError:1.3";

    public static DevicesDocument ParseDevices(string xml)
    {
        var root = LoadRoot(xml, DevicesNs + "MTConnectDevices");
        var instanceId = ParseLong(RequiredHeader(root, DevicesNs), "instanceId");

        var devices = root.Descendants(DevicesNs + "Device").ToList();
        var dataItems = new Dictionary<string, DataItemInfo>();
        foreach (var item in root.Descendants(DevicesNs + "DataItem"))
        {
            var id = RequiredAttribute(item, "id");
            dataItems[id] = new DataItemInfo(
                id,
                RequiredAttribute(item, "category"),
                RequiredAttribute(item, "type"),
                (string?)item.Attribute("subType"),
                (string?)item.Attribute("units"));
        }

        return new DevicesDocument(instanceId, devices.Select(d => RequiredAttribute(d, "name")).ToList(), dataItems);
    }

    public static StreamsDocument ParseStreams(string xml)
    {
        var root = LoadRoot(xml, StreamsNs + "MTConnectStreams");
        var headerElement = RequiredHeader(root, StreamsNs);
        var header = new StreamsHeader(
            ParseLong(headerElement, "instanceId"),
            ParseLong(headerElement, "nextSequence"),
            ParseLong(headerElement, "firstSequence"),
            ParseLong(headerElement, "lastSequence"),
            ParseLong(headerElement, "bufferSize"));

        var observations = new List<MTConnectObservation>();
        foreach (var device in root.Descendants(StreamsNs + "DeviceStream"))
        {
            var deviceName = RequiredAttribute(device, "name");
            foreach (var group in device.Elements(StreamsNs + "ComponentStream").Elements())
            {
                var category = group.Name.LocalName switch
                {
                    "Samples" => ObservationCategory.Sample,
                    "Events" => ObservationCategory.Event,
                    "Condition" => ObservationCategory.Condition,
                    _ => (ObservationCategory?)null,
                };
                if (category is null)
                {
                    continue;
                }

                observations.AddRange(group.Elements().Select(e => ParseObservation(e, deviceName, header.InstanceId, category.Value)));
            }
        }

        observations.Sort((a, b) => a.Sequence.CompareTo(b.Sequence));
        return new StreamsDocument(header, observations);
    }

    /// <summary>Returns the agent error when the document is MTConnectError; null for other documents.</summary>
    public static MTConnectAgentErrorException? TryParseError(string xml)
    {
        XElement root;
        try
        {
            root = XDocument.Parse(xml).Root!;
        }
        catch (XmlException)
        {
            return null;
        }

        if (root.Name != ErrorNs + "MTConnectError")
        {
            return null;
        }

        var error = root.Descendants(ErrorNs + "Error").FirstOrDefault();
        return new MTConnectAgentErrorException(
            (string?)error?.Attribute("errorCode") ?? "UNKNOWN",
            error?.Value.Trim() ?? "");
    }

    private static MTConnectObservation ParseObservation(XElement element, string deviceName, long instanceId, ObservationCategory category)
    {
        var isCondition = category == ObservationCategory.Condition;
        ConditionLevel? level = null;
        if (isCondition)
        {
            level = Enum.TryParse<ConditionLevel>(element.Name.LocalName, ignoreCase: false, out var parsed)
                ? parsed
                : throw new MTConnectFormatException($"Unknown condition level '{element.Name.LocalName}'.");
        }

        return new MTConnectObservation(
            deviceName,
            instanceId,
            RequiredAttribute(element, "dataItemId"),
            ParseLong(element, "sequence"),
            ParseTimestamp(element),
            category,
            isCondition ? (string?)element.Attribute("type") : ToUpperSnakeCase(element.Name.LocalName),
            (string?)element.Attribute("subType"),
            null,
            element.Value.Trim(),
            level,
            isCondition ? NullIfEmpty((string?)element.Attribute("nativeCode")) : null);
    }

    private static XElement LoadRoot(string xml, XName expectedRoot)
    {
        XElement root;
        try
        {
            root = XDocument.Parse(xml).Root ?? throw new MTConnectFormatException("Empty document.");
        }
        catch (XmlException ex)
        {
            throw new MTConnectFormatException("Response is not well-formed XML.", ex);
        }

        if (root.Name != expectedRoot)
        {
            throw new MTConnectFormatException(
                $"Expected {expectedRoot.LocalName} ({expectedRoot.NamespaceName}), got {root.Name.LocalName} ({root.Name.NamespaceName}).");
        }
        return root;
    }

    private static XElement RequiredHeader(XElement root, XNamespace ns) =>
        root.Element(ns + "Header") ?? throw new MTConnectFormatException("Missing Header element.");

    private static string RequiredAttribute(XElement element, string name) =>
        (string?)element.Attribute(name) is { Length: > 0 } value
            ? value
            : throw new MTConnectFormatException($"Missing attribute '{name}' on {element.Name.LocalName}.");

    private static long ParseLong(XElement element, string name) =>
        long.TryParse(RequiredAttribute(element, name), NumberStyles.None, CultureInfo.InvariantCulture, out var value)
            ? value
            : throw new MTConnectFormatException($"Attribute '{name}' on {element.Name.LocalName} is not a number.");

    private static DateTimeOffset ParseTimestamp(XElement element) =>
        DateTimeOffset.TryParse(RequiredAttribute(element, "timestamp"), CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var value)
            ? value
            : throw new MTConnectFormatException($"Attribute 'timestamp' on {element.Name.LocalName} is not a date.");

    /// <summary>PathFeedrate -> PATH_FEEDRATE (MTConnect element name to data item type).</summary>
    private static string ToUpperSnakeCase(string elementName)
    {
        var builder = new StringBuilder(elementName.Length + 4);
        for (var i = 0; i < elementName.Length; i++)
        {
            if (i > 0 && char.IsUpper(elementName[i]))
            {
                builder.Append('_');
            }
            builder.Append(char.ToUpperInvariant(elementName[i]));
        }
        return builder.ToString();
    }

    private static string? NullIfEmpty(string? value) => string.IsNullOrEmpty(value) ? null : value;
}
