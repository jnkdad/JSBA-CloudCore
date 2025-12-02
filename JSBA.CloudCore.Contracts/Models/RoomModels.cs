// RoomModels.cs
// Data models for the CloudCore PDF→Rooms extraction engine.

using System.Collections.Generic;

namespace JSBA.CloudCore.Contracts.Models
{
    public class RoomsResult
    {
        public List<RoomModel> Rooms { get; set; } = new();
        public RoomsMetadata Metadata { get; set; } = new();
    }

    public class RoomModel
    {
        public string Id { get; set; } = string.Empty;
        public string? Name { get; set; }
        public List<Point2D> Polygon { get; set; } = new();
    }

    public class Point2D
    {
        public double X { get; set; }
        public double Y { get; set; }
    }

    public class RoomsMetadata
    {
        public string Units { get; set; } = "feet";
        public string? Scale { get; set; }
        public int PageCount { get; set; } = 1;
    }

    public class PdfOptions
    {
        /// <summary>
        /// Optional PDF filename for loading PDF-specific extraction settings
        /// </summary>
        public string? FileName { get; set; }

        // placeholder for future configuration (page index, tolerances, etc.)
    }

    public class BoundaryComparisonResult
    {
        public List<List<Point2D>> PdfPigBoundaries { get; set; } = new();
        public List<List<Point2D>> PDFiumNativeBoundaries { get; set; } = new();
    }

    public class TextComparisonResult
    {
        public List<RoomLabel> PdfPigLabels { get; set; } = new();
        public List<RoomLabel> PDFiumNativeLabels { get; set; } = new();
    }

    public class RoomLabel
    {
        public string Text { get; set; } = string.Empty;
        public double CenterX { get; set; }
        public double CenterY { get; set; }
    }

}
