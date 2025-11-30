using System.Collections.Generic;

namespace JSBA.CloudCore.Contracts
{
    // Top-level document produced by the extractor
    public class RimDocument
    {
        public string SourceId { get; set; } = "";
        public List<RimGeometry> Points { get; set; } = new();
        public List<RimText> Texts { get; set; } = new();
    }

    // Minimal geometry placeholder (extend later)
    public record RimGeometry(double X, double Y, double Z, string Units);

    // Minimal text element
    public record RimText(string Value, double X, double Y, double RotationDeg);
}
