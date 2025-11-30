using System;
using System.Collections.Generic;

namespace JSBA.CloudCore.Contracts
{
    // Basic geometric point
    public record RimPoint(double X, double Y, double Z = 0);

    // Polygon boundary
    public record RimPoly(IEnumerable<RimPoint> Vertices);

    // A single room or acoustic space
    public record RimRoom(string Id, string Name, RimPoly Boundary);

    // Standard request/response envelopes
    public record ExtractionRequest(string SourceType, string InputPathOrBlobUrl);
    public record ExtractionResult(bool Ok, RimDocument? Doc, string? Message = null);

    public record ConvertRequest(string Target, RimDocument Doc);
    public record ConvertResult(bool Ok, string OutputPathOrPayload, string? Message = null);

    public record BuildRequest(string Target, RimDocument Doc, IDictionary<string, string>? Options = null);
    public record BuildResult(bool Ok, string OutputPath, string? Message = null);
}
