namespace CommunicationModule.Api.Endpoints;
using CommunicationModule.Api.Services;

public static class Hl7Endpoints
{
    public static IEndpointRouteBuilder MapHl7Endpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/hl7");

        group.MapPost("/", async (
            HttpRequest request,
            AppointmentIngestionService ingestion,
            CancellationToken ct) =>
        {
            using var reader = new StreamReader(request.Body);
            var hl7 = await reader.ReadToEndAsync(ct);

            if (string.IsNullOrWhiteSpace(hl7))
                return Results.BadRequest("Empty HL7 message");

            // Parse HL7
            var parsed = Hl7Parser.Parse(hl7);

            // Map naar FHIR (jouw bestaande flow)
            var fhirJson = Hl7ToFhirMapper.MapToAppointment(parsed);

            // Gebruik jouw bestaande ingestie + ACK flow
            var result = await ingestion.IngestAsync(
                fhirJson,
                parsed.OrganisationId,
                ct);

            if (!result.Success)
            {
                return Results.BadRequest(result.Error ?? "HL7 message could not be processed.");
            }

            return Results.Ok(new
            {
                message = "HL7 message processed",
                appointmentId = result.AppointmentId,
                scheduled = result.Scheduled
            });
        });

        return app;
    }
}