// ExtractorData.cs
// Data models used during PDF extraction processing
// These are not part of the public API contract

using System.Collections.Generic;
using JSBA.CloudCore.Contracts.Models;

namespace JSBA.CloudCore.Extractor
{
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
    /// Internal representation of a room boundary during extraction
    /// </summary>
    public class RoomBoundary
    {
        public List<Point2D> Polygon { get; set; } = new();
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
}

