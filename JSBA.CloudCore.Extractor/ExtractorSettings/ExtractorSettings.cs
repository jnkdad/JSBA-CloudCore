// ExtractorSettings.cs
// Settings classes for configuring PDF extraction behavior
// These are not part of the public API contract

using System.Collections.Generic;
using System.IO;

namespace JSBA.CloudCore.Extractor
{
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

