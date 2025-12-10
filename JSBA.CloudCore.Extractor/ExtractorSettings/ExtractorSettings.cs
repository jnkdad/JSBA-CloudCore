// ExtractorSettings.cs
// Settings classes for configuring PDF extraction behavior
// These are not part of the public API contract
//
// NOTE: The filtering and statistics features in these settings are intended to support
// later statistics analysis and ML (machine learning) analysis. The path statistics,
// line width distributions, and filtering capabilities are designed to enable data-driven
// improvements to extraction algorithms and quality assessment.

using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

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
        public Dictionary<string, ExtractionSettings> PdfTypes { get; set; } = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Get settings for a specific PDF file, falling back to default if not found
        /// </summary>
        /// <param name="pdfFileName">PDF filename (e.g., "Project3_onlywall.pdf")</param>
        /// <returns>Settings for the PDF or default settings</returns>
        public ExtractionSettings GetSettingsForPdf(string pdfFileName)
        {
            if (string.IsNullOrWhiteSpace(pdfFileName))
                return Default;

            // Try to find exact match (dictionary is case-insensitive)
            if (PdfTypes.TryGetValue(pdfFileName, out var settings))
                return settings;

            // Try to find by filename without path (dictionary is case-insensitive)
            var fileNameOnly = Path.GetFileName(pdfFileName);
            if (PdfTypes.TryGetValue(fileNameOnly, out settings))
                return settings;

            // Fallback to default
            return Default;
        }
    }

    /// <summary>
    /// Manager for extraction settings that loads and caches settings collection as a singleton
    /// </summary>
    public class ExtractionSettingsManager
    {
        private static readonly Lazy<ExtractionSettingsManager> _instance = 
            new Lazy<ExtractionSettingsManager>(() => new ExtractionSettingsManager(), System.Threading.LazyThreadSafetyMode.ExecutionAndPublication);
        private static readonly object _lock = new object();
        private ExtractionSettingsCollection? _cachedCollection;
        private string? _cachedSettingsPath;

        private ExtractionSettingsManager() { }

        /// <summary>
        /// Gets the singleton instance of the settings manager
        /// </summary>
        public static ExtractionSettingsManager Instance => _instance.Value;

        /// <summary>
        /// Gets the cached settings collection, loading it if necessary
        /// </summary>
        /// <param name="settingsPath">Path to the settings JSON file. If null or empty, defaults to "ExtractorSettings/extraction-settings.json" in the base directory.</param>
        /// <returns>The settings collection</returns>
        public ExtractionSettingsCollection GetCollection(string? settingsPath = null)
        {
            // Default to standard location if not provided
            if (string.IsNullOrWhiteSpace(settingsPath))
            {
                settingsPath = Path.Combine(AppContext.BaseDirectory, "ExtractorSettings", "extraction-settings.json");
            }

            // If we already have a cached collection for this path, return it
            if (_cachedCollection != null && _cachedSettingsPath == settingsPath)
            {
                return _cachedCollection;
            }

            // Load and cache the collection
            lock (_lock)
            {
                // Double-check after acquiring lock
                if (_cachedCollection != null && _cachedSettingsPath == settingsPath)
                {
                    return _cachedCollection;
                }

                _cachedCollection = LoadCollectionFromFile(settingsPath);
                _cachedSettingsPath = settingsPath;
                return _cachedCollection;
            }
        }

        /// <summary>
        /// Get settings for a specific PDF file
        /// </summary>
        /// <param name="pdfFileName">PDF filename (e.g., "Project3_onlywall.pdf")</param>
        /// <param name="settingsPath">Path to the settings JSON file. If null or empty, defaults to "ExtractorSettings/extraction-settings.json" in the base directory.</param>
        /// <returns>Settings for the PDF or default settings</returns>
        public ExtractionSettings GetSettingsForPdf(string pdfFileName, string? settingsPath = null)
        {
            var collection = GetCollection(settingsPath);
            return collection.GetSettingsForPdf(pdfFileName);
        }

        /// <summary>
        /// Clears the cached collection, forcing a reload on next access
        /// </summary>
        public void ClearCache()
        {
            lock (_lock)
            {
                _cachedCollection = null;
                _cachedSettingsPath = null;
            }
        }

        private ExtractionSettingsCollection LoadCollectionFromFile(string settingsPath)
        {
            try
            {
                if (!File.Exists(settingsPath))
                {
                    return new ExtractionSettingsCollection();
                }

                string json = File.ReadAllText(settingsPath);
                var settingsCollection = JsonSerializer.Deserialize<ExtractionSettingsCollection>(json, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                if (settingsCollection == null)
                {
                    return new ExtractionSettingsCollection();
                }

                // Ensure the dictionary uses case-insensitive comparer (JSON deserialization doesn't preserve it)
                if (settingsCollection.PdfTypes.Comparer != StringComparer.OrdinalIgnoreCase)
                {
                    var caseInsensitiveDict = new Dictionary<string, ExtractionSettings>(StringComparer.OrdinalIgnoreCase);
                    foreach (var kvp in settingsCollection.PdfTypes)
                    {
                        caseInsensitiveDict[kvp.Key] = kvp.Value;
                    }
                    settingsCollection.PdfTypes = caseInsensitiveDict;
                }

                return settingsCollection;
            }
            catch (Exception)
            {
                return new ExtractionSettingsCollection();
            }
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
        public double MinArea { get; set; } = 0; // Minimum polygon area (0 = no filter)
        public double MinWidth { get; set; } = 0; // Minimum polygon width (0 = no filter, filters thin strips)
        public double WallThickness { get; set; } = 0; // Wall thickness for collapsing parallel lines (0 = disabled)
        public bool SkipCollapseParallelWalls { get; set; } = false; // Skip collapsing parallel walls, keep both inner and outer lines to help find separate room boundaries
    }

    public class RoomSizeSettings
    {
        public bool Enabled { get; set; } = true;
        public int MinRoomCount { get; set; } = 1;  // Minimum expected number of rooms
        public int MaxRoomCount { get; set; } = 30; // Maximum expected number of rooms
    }
}

