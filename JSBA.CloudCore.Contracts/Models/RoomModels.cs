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

    public class RoomBoundary
    {
        public List<Point2D> Polygon { get; set; } = new();
    }

    /// <summary>
    /// Represents a raw path extracted from PDF (for debugging and visualization)
    /// </summary>
    public class RawPath
    {
        public int ObjectIndex { get; set; }
        public List<Point2D> Points { get; set; } = new();
        public double LineWidth { get; set; }
        public bool IsStroked { get; set; }
        public bool IsFilled { get; set; }
        public int SegmentCount { get; set; }
        public double PathLength { get; set; }
        public string PathType { get; set; } = "Unknown"; // "Line", "Rectangle", "Curve", etc.
        public string? Layer { get; set; } // Layer/OCG name if available
    }

    /// <summary>
    /// Statistics about path distributions in a PDF page
    /// </summary>
    public class PathStatistics
    {
        /// <summary>
        /// Map of line width to list of paths with that width
        /// Key: line width, Value: list of paths
        /// </summary>
        public Dictionary<double, List<RawPath>> LineWidthDistribution { get; set; } = new();

        /// <summary>
        /// Map of length range to list of paths in that range
        /// Key: range description (e.g., "0-50"), Value: list of paths
        /// </summary>
        public Dictionary<string, List<RawPath>> LengthRangeDistribution { get; set; } = new();

        /// <summary>
        /// Map of layer name to list of paths on that layer
        /// Key: layer name, Value: list of paths
        /// </summary>
        public Dictionary<string, List<RawPath>> LayerDistribution { get; set; } = new();

        /// <summary>
        /// Sorted list of line widths (descending - thickest first)
        /// </summary>
        public List<double> SortedLineWidths { get; set; } = new();

        /// <summary>
        /// Sorted list of length range keys
        /// </summary>
        public List<string> SortedLengthRanges { get; set; } = new();

        /// <summary>
        /// Total number of paths analyzed
        /// </summary>
        public int TotalPaths { get; set; }

        /// <summary>
        /// Minimum path length found
        /// </summary>
        public double MinLength { get; set; }

        /// <summary>
        /// Maximum path length found
        /// </summary>
        public double MaxLength { get; set; }

        /// <summary>
        /// Average path length
        /// </summary>
        public double AverageLength { get; set; }

        /// <summary>
        /// Minimum line width found
        /// </summary>
        public double MinLineWidth { get; set; }

        /// <summary>
        /// Maximum line width found
        /// </summary>
        public double MaxLineWidth { get; set; }

        /// <summary>
        /// Average line width
        /// </summary>
        public double AverageLineWidth { get; set; }

        /// <summary>
        /// Page width (for room size calculations)
        /// </summary>
        public double PageWidth { get; set; }

        /// <summary>
        /// Page height (for room size calculations)
        /// </summary>
        public double PageHeight { get; set; }
    }

    /// <summary>
    /// Collection of extraction settings for different PDF types
    /// </summary>
    public class ExtractionSettingsCollection
    {
        /// <summary>
        /// Default settings used when no PDF-specific settings are found
        /// </summary>
        public ExtractionSettings Default { get; set; } = new();

        /// <summary>
        /// PDF-specific settings keyed by PDF filename (case-insensitive)
        /// </summary>
        public Dictionary<string, ExtractionSettings> PdfTypes { get; set; } = new();

        /// <summary>
        /// Get settings for a specific PDF file, falling back to default if not found
        /// </summary>
        /// <param name="pdfFileName">PDF filename (e.g., "Project3_onlywall.pdf")</param>
        /// <returns>Settings for the PDF or default settings</returns>
        public ExtractionSettings GetSettingsForPdf(string pdfFileName)
        {
            if (string.IsNullOrWhiteSpace(pdfFileName))
                return Default;

            // Normalize the filename to lowercase for case-insensitive matching
            var normalizedName = pdfFileName.ToLowerInvariant();

            // Try to find exact match
            if (PdfTypes.TryGetValue(normalizedName, out var settings))
                return settings;

            // Try to find by filename without path
            var fileNameOnly = Path.GetFileName(normalizedName);
            if (PdfTypes.TryGetValue(fileNameOnly, out settings))
                return settings;

            // Fallback to default
            return Default;
        }
    }

    /// <summary>
    /// Settings for filtering and extracting paths from PDFs
    /// </summary>
    public class ExtractionSettings
    {
        public PathFilters Filters { get; set; } = new();
        public PolygonSettings Polygon { get; set; } = new();
        public RoomSizeSettings RoomSize { get; set; } = new();
    }

    public class PathFilters
    {
        public LineWidthFilter LineWidth { get; set; } = new();
        public LengthFilter Length { get; set; } = new();
        public ShapeFilter Shape { get; set; } = new();
        public SingleSegmentFilter SingleSegment { get; set; } = new();
    }

    public class LineWidthFilter
    {
        public bool Enabled { get; set; } = true;
        public double MinWidth { get; set; } = 0.5;
        public double MaxWidth { get; set; } = 10.0;
    }

    public class LengthFilter
    {
        public bool Enabled { get; set; } = true;
        public double MinLength { get; set; } = 500.0; // Minimum path length to consider
    }

    public class ShapeFilter
    {
        public bool Enabled { get; set; } = false;
        public List<string> AllowedShapes { get; set; } = new() { "line", "rectangle" };
    }

    public class SingleSegmentFilter
    {
        public bool Enabled { get; set; } = false;
        public double Offset { get; set; } = 100.0; // Offset for single segment filtering
    }

    public class PolygonSettings
    {
        public bool RemoveNested { get; set; } = false; // Remove inner polygons
        public bool RemoveOuter { get; set; } = true;   // Remove outer polygons (keep inner)
        public double GapTolerance { get; set; } = 50; // Tolerance for connecting segments
    }

    public class RoomSizeSettings
    {
        public bool Enabled { get; set; } = true;
        public int MinRoomCount { get; set; } = 1;  // Minimum expected number of rooms
        public int MaxRoomCount { get; set; } = 30; // Maximum expected number of rooms
    }
}
