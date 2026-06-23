namespace CommunicationModule.Api.Services;
public static class Hl7Parser
{
    public static ParsedHl7Message Parse(string message)
    {
        var result = new ParsedHl7Message();

        var segments = message.Split('\r');

        foreach (var segment in segments)
        {
            var fields = segment.Split('|');

            if (fields.Length == 0)
                continue;

            result.AppointmentDateTime ??= TryParseDateTime(fields);
        }

        foreach (var segment in segments)
        {
            var fields = segment.Split('|');

            if (fields.Length == 0)
                continue;

            switch (fields[0])
            {
                case "MSH":
                    result.MessageId = fields.ElementAtOrDefault(9) ?? string.Empty;
                    result.OrganisationId = Guid.Parse("11111111-1111-1111-1111-111111111111");
                    break;

                case "PID":
                    result.PatientId = fields.ElementAtOrDefault(3) ?? string.Empty;

                    var name = (fields.ElementAtOrDefault(5) ?? string.Empty).Split('^');
                    result.LastName = name.ElementAtOrDefault(0) ?? "";
                    result.FirstName = name.ElementAtOrDefault(1) ?? "";
                    result.PhoneNumber = ExtractPhoneNumber(fields);
                    break;

                case "SCH":
                    result.Location = fields.ElementAtOrDefault(8) ?? result.Location;
                    result.AppointmentDateTime ??= TryParseDateTime(fields);
                    break;
            }
        }

        result.AppointmentDateTime ??= DateTimeOffset.UtcNow.AddDays(1);

        return result;
    }

    private static string ExtractPhoneNumber(string[] fields)
    {
        foreach (var index in new[] { 13, 14, 15 })
        {
            var value = fields.ElementAtOrDefault(index);
            if (!string.IsNullOrWhiteSpace(value))
                return value;
        }

        return string.Empty;
    }

    private static DateTimeOffset? TryParseDateTime(string[] fields)
    {
        foreach (var value in fields.Skip(1))
        {
            if (TryParseDateTime(value, out var dateTime))
                return dateTime;
        }

        return null;
    }

    private static bool TryParseDateTime(string? value, out DateTimeOffset result)
    {
        result = default;

        if (string.IsNullOrWhiteSpace(value))
            return false;

        var formats = new[]
        {
            "yyyyMMddHHmmsszzz",
            "yyyyMMddHHmmsszz",
            "yyyyMMddHHmmss",
            "yyyyMMddHHmm",
            "yyyyMMddHH",
            "yyyyMMdd"
        };

        if (DateTimeOffset.TryParseExact(
                value,
                formats,
                System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.AssumeUniversal,
                out result))
        {
            return true;
        }

        return DateTimeOffset.TryParse(value, out result);
    }
}