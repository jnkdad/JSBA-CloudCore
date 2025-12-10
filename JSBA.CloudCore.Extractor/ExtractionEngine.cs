// ExtractionEngine.cs
// CloudCore PDF→Rooms extraction engine with PdfPig (text) and PDFium Native (boundaries) support.

// TODO: Planned Refactor
// This class serves as a functional baseline. Future refactoring should:
// 1. Break the engine into smaller, focused components:
//    - Input parsing (PDF stream handling, document loading)
//    - Geometry processing (path extraction, point manipulation)
//    - Polygonization (gap bridging, polygon reconstruction)
//    - Filtering (path filtering, statistics generation)
//    - Label matching (text extraction, room label matching)
// 2. Isolate PDFium and NTS-specific implementations behind clearer internal abstractions:
//    - Create interfaces/abstractions for PDF parsing (IPdfParser, IPathExtractor)
//    - Create interfaces/abstractions for geometry operations (IPolygonizer, IGapBridger)
//    - Allow swapping implementations without changing the core extraction logic
// 3. Trim dead code and experimental branches:
//    - Remove unused methods (e.g., ExtractRoomBoundariesWithPDFiumSharp placeholder)
//    - Remove or consolidate duplicate functionality
//    - Remove commented-out code and experimental branches that won't be supported
// See separate issue for tracking this refactor.

using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Text.Json;
using JSBA.CloudCore.Contracts.Models;
using Microsoft.Extensions.Logging;
using PdfPig = UglyToad.PdfPig;
using UglyToad.PdfPig.Content;
using JSBA.CloudCore.Extractor.Helpers;

namespace JSBA.CloudCore.Extractor
{
    public class ExtractionEngine
    {
        private readonly ILogger<ExtractionEngine> _logger;
        private readonly NtsPolygonizer _ntsPolygonizer;

        /// <summary>
        /// All raw paths extracted from the PDF during the last extraction operation.
        /// Populated by ExtractRoomBoundariesWithPDFiumNative and ExtractTextWithPDFiumNative.
        /// Useful for debugging, visualization, and filtering.
        /// </summary>
        public List<RawPath> AllPaths { get; private set; } = new();

        /// <summary>
        /// Paths filtered
        /// Useful for debugging, visualization
        /// </summary>
        public List<RawPath> FilteredPaths { get; private set; } = new();

        /// <summary>
        /// Closed polygons reconstructed from raw paths during the last extraction operation.
        /// Populated by ExtractRoomBoundariesWithPDFiumNative after calling ReconstructClosedPolygons.
        /// Useful for debugging and visualization.
        /// </summary>
        public List<List<Point2D>> ClosedPolygons { get; private set; } = new();

        /// <summary>
        /// Page dimensions from the last extraction operation.
        /// </summary>
        public double PageWidth { get; private set; }
        public double PageHeight { get; private set; }

        public ExtractionEngine(ILogger<ExtractionEngine> logger)
        {
            _logger = logger;

            // Create NTS polygonizer with its own logger
            var ntsLoggerFactory = LoggerFactory.Create(builder => builder.AddConsole().SetMinimumLevel(LogLevel.Information));
            var ntsLogger = ntsLoggerFactory.CreateLogger<NtsPolygonizer>();
            _ntsPolygonizer = new NtsPolygonizer(ntsLogger);
        }

        public RoomsResult ProcessPdfToRooms(Stream pdfStream, PdfOptions options)
        {
            _logger.LogInformation("Starting PDF processing for file: {FileName}", options.FileName ?? "unknown");

            // Copy stream to memory since we may need to read it multiple times
            using var memoryStream = new MemoryStream();
            pdfStream.CopyTo(memoryStream);
            memoryStream.Position = 0;

            // Use hybrid approach:
            // 1. PdfPig for text extraction (room labels)
            // 2. PDFiumSharp for vector graphics extraction (room boundaries)
            var result = ProcessWithHybridApproach(memoryStream, options.FileName);

            _logger.LogInformation("PDF processing completed. Extracted {RoomCount} rooms", result.Rooms.Count);
            return result;
        }

        /// <summary>
        /// Compare boundary extraction results between PdfPig and PDFium Native methods
        /// Returns a comparison result with boundaries from both methods
        /// </summary>
        public BoundaryComparisonResult CompareBoundaryExtractionMethods(Stream pdfStream)
        {
            _logger.LogInformation("Starting boundary extraction comparison");

            // Copy stream to memory since we need to read it multiple times
            using var memoryStream = new MemoryStream();
            pdfStream.CopyTo(memoryStream);

            // Extract with PdfPig
            memoryStream.Position = 0;
            var pdfPigBoundaries = ExtractRoomBoundariesWithPdfPig(memoryStream);

            // Extract with PDFium Native
            memoryStream.Position = 0;
            var pdfiumBoundaries = ExtractRoomBoundariesWithPDFiumNative(memoryStream);

            var result = new BoundaryComparisonResult
            {
                PdfPigBoundaries = pdfPigBoundaries.Select(b => b.Polygon).ToList(),
                PDFiumNativeBoundaries = pdfiumBoundaries.Select(b => b.Polygon).ToList()
            };

            _logger.LogInformation("Comparison complete. PdfPig: {PdfPigCount}, PDFium: {PDFiumCount}",
                result.PdfPigBoundaries.Count, result.PDFiumNativeBoundaries.Count);

            return result;
        }

        /// <summary>
        /// Compare text extraction between PdfPig and PDFium Native
        /// </summary>
        public TextComparisonResult CompareTextExtractionMethods(Stream pdfStream)
        {
            _logger.LogInformation("Starting text extraction comparison");

            // Copy stream to memory since we need to read it multiple times
            using var memoryStream = new MemoryStream();
            pdfStream.CopyTo(memoryStream);

            // Extract with PdfPig
            memoryStream.Position = 0;
            var pdfPigLabels = ExtractRoomLabelsWithPdfPig(memoryStream);

            // Extract with PDFium Native
            memoryStream.Position = 0;
            var pdfiumLabels = ExtractTextWithPDFiumNative(memoryStream);

            var result = new TextComparisonResult
            {
                PdfPigLabels = pdfPigLabels,
                PDFiumNativeLabels = pdfiumLabels
            };

            _logger.LogInformation("Text comparison complete. PdfPig: {PdfPigCount}, PDFium: {PDFiumCount}",
                result.PdfPigLabels.Count, result.PDFiumNativeLabels.Count);

            return result;
        }

        /// <summary>
        /// Hybrid approach: Use PdfPig for text extraction and boundary extraction
        /// </summary>
        /// <param name="pdfStream">PDF stream</param>
        /// <param name="pdfFileName">Optional PDF filename for loading PDF-specific settings</param>
        private RoomsResult ProcessWithHybridApproach(MemoryStream pdfStream, string? pdfFileName = null)
        {
            _logger.LogInformation("Processing PDF with hybrid approach (PdfPig for text and boundaries)");

            var result = new RoomsResult();

            try
            {
                // Load extraction settings (PDF-specific or default)
                ExtractionSettings? settings = null;

                if (!string.IsNullOrWhiteSpace(pdfFileName))
                {
                    // Load PDF-specific settings (settingsPath will default if not provided)
                    settings = LoadSettingsForPdf(pdfFileName);
                }
                else
                {
                    // Load default settings
                    var settingsPath = Path.Combine(AppContext.BaseDirectory, "ExtractorSettings", "extraction-settings.json");
                    settings = LoadSettings(settingsPath);
                    _logger.LogInformation("Loaded default extraction settings from {Path}", settingsPath);
                }

                // Step 1: Extract text labels using PdfPig
                var roomLabels = ExtractRoomLabelsWithPdfPig(pdfStream);
                _logger.LogInformation("Extracted {LabelCount} room labels using PdfPig", roomLabels.Count);

                // Step 2: Extract room boundaries using PDFium Native with settings
                pdfStream.Position = 0; // Reset stream

                //var roomBoundaries = ExtractRoomBoundariesWithPdfPig(pdfStream);
                var roomBoundaries = ExtractRoomBoundariesWithPDFiumNative(pdfStream, settings);
                _logger.LogInformation("Extracted {BoundaryCount} room boundaries using PDFium Native", roomBoundaries.Count);

                // Step 3: Match labels with boundaries
                result.Rooms = MatchLabelsWithBoundaries(roomLabels, roomBoundaries);
                _logger.LogInformation("Matched {RoomCount} rooms", result.Rooms.Count);

                // Set metadata
                pdfStream.Position = 0;
                using (var document = PdfPig.PdfDocument.Open(pdfStream))
                {
                    result.Metadata.PageCount = document.NumberOfPages;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing PDF with hybrid approach");
                throw;
            }

            return result;
        }

        /// <summary>
        /// Extract room labels using PdfPig (text extraction)
        /// </summary>
        private List<RoomLabel> ExtractRoomLabelsWithPdfPig(MemoryStream pdfStream)
        {
            var labels = new List<RoomLabel>();

            using (var document = PdfPig.PdfDocument.Open(pdfStream))
            {
                // Process first page for now
                var page = document.GetPage(1);
                var words = page.GetWords().ToList();

                _logger.LogInformation("Found {WordCount} words on page 1", words.Count);

                // Find words that look like room labels
                var roomTexts = words.Where(w =>
                    w.Text.Contains("ROOM", StringComparison.OrdinalIgnoreCase) ||
                    w.Text.Contains("OFFICE", StringComparison.OrdinalIgnoreCase) ||
                    w.Text.Contains("CLASSROOM", StringComparison.OrdinalIgnoreCase) ||
                    w.Text.Contains("CORRIDOR", StringComparison.OrdinalIgnoreCase) ||
                    w.Text.Contains("HALL", StringComparison.OrdinalIgnoreCase) ||
                    w.Text.Contains("STORAGE", StringComparison.OrdinalIgnoreCase) ||
                    w.Text.Contains("LOBBY", StringComparison.OrdinalIgnoreCase)
                ).ToList();

                foreach (var word in roomTexts)
                {
                    var bbox = word.BoundingBox;
                    labels.Add(new RoomLabel
                    {
                        Text = word.Text,
                        CenterX = bbox.Left + bbox.Width / 2,
                        CenterY = bbox.Bottom + bbox.Height / 2
                    });
                }
            }

            return labels;
        }

        /// <summary>
        /// Extract room boundaries using PDFiumSharp (vector graphics extraction)
        /// NOTE: PDFiumSharp v1.4660.0-alpha1 does not expose the page object API needed for path extraction
        /// This method is a placeholder for future implementation when the API is available
        /// </summary>
        private List<RoomBoundary> ExtractRoomBoundariesWithPDFiumSharp(MemoryStream pdfStream)
        {
            var boundaries = new List<RoomBoundary>();

            try
            {
                _logger.LogInformation("PDFiumSharp boundary extraction not yet implemented");
                _logger.LogInformation("PDFiumSharp v1.4660.0-alpha1 does not expose FPDFPage_GetObject API");

                // TODO: Implement when PDFiumSharp exposes page object API
                // Required API:
                // - FPDFPage_CountObjects()
                // - FPDFPage_GetObject()
                // - FPDFPageObj_GetType()
                // - FPDFPath_CountSegments()
                // - FPDFPathSegment_GetPoint()
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in PDFiumSharp boundary extraction");
            }

            return boundaries;
        }

        /// <summary>
        /// Extract room boundaries using native PDFium library (direct P/Invoke)
        /// Uses the native PDFium API to extract vector paths from PDF page objects
        /// Populates AllPaths, PageWidth, and PageHeight properties during extraction
        /// </summary>
        public List<RoomBoundary> ExtractRoomBoundariesWithPDFiumNative(Stream pdfStream)
        {
            return ExtractRoomBoundariesWithPDFiumNative(pdfStream, null);
        }

        /// <summary>
        /// Extract room boundaries using native PDFium library with optional settings
        /// Applies filtering and gap bridging based on settings
        /// </summary>
        /// <param name="pdfStream">PDF stream to extract from</param>
        /// <param name="settings">Extraction settings (null for default behavior)</param>
        public List<RoomBoundary> ExtractRoomBoundariesWithPDFiumNative(Stream pdfStream, ExtractionSettings? settings)
        {
            var boundaries = new List<RoomBoundary>();

            try
            {
                _logger.LogInformation("PDFium Native: Starting boundary extraction");

                // Initialize PDFium library
                PDFiumNative.FPDF_InitLibrary();

                // Load PDF from stream
                pdfStream.Position = 0;
                byte[] pdfBytes;
                if (pdfStream is MemoryStream memStream)
                {
                    pdfBytes = memStream.ToArray();
                }
                else
                {
                    using var ms = new MemoryStream();
                    pdfStream.CopyTo(ms);
                    pdfBytes = ms.ToArray();
                }
                IntPtr document = PDFiumNative.FPDF_LoadMemDocument(pdfBytes, pdfBytes.Length, null);

                if (document == IntPtr.Zero)
                {
                    uint error = PDFiumNative.FPDF_GetLastError();
                    _logger.LogError("PDFium Native: Failed to load document. Error code: {ErrorCode}", error);
                    PDFiumNative.FPDF_DestroyLibrary();
                    return boundaries;
                }

                int pageCount = PDFiumNative.FPDF_GetPageCount(document);
                _logger.LogInformation("PDFium Native: Document has {PageCount} pages", pageCount);

                if (pageCount == 0)
                {
                    _logger.LogWarning("PDFium Native: No pages in document");
                    PDFiumNative.FPDF_CloseDocument(document);
                    PDFiumNative.FPDF_DestroyLibrary();
                    return boundaries;
                }

                // Process first page
                IntPtr page = PDFiumNative.FPDF_LoadPage(document, 0);
                if (page == IntPtr.Zero)
                {
                    _logger.LogError("PDFium Native: Failed to load page 0");
                    PDFiumNative.FPDF_CloseDocument(document);
                    PDFiumNative.FPDF_DestroyLibrary();
                    return boundaries;
                }

                PageWidth = PDFiumNative.FPDF_GetPageWidth(page);
                PageHeight = PDFiumNative.FPDF_GetPageHeight(page);
                _logger.LogInformation("PDFium Native: Page dimensions: {Width} x {Height}", PageWidth, PageHeight);

                // Get all page objects
                int objectCount = PDFiumNative.FPDFPage_CountObjects(page);
                _logger.LogInformation("PDFium Native: Found {ObjectCount} page objects", objectCount);

                // Clear and populate AllPaths with raw path data
                AllPaths.Clear();

                for (int i = 0; i < objectCount; i++)
                {
                    IntPtr pageObj = PDFiumNative.FPDFPage_GetObject(page, i);
                    if (pageObj == IntPtr.Zero)
                        continue;

                    int objType = PDFiumNative.FPDFPageObj_GetType(pageObj);

                    // Only process path objects (type 2)
                    if (objType == PDFiumNative.FPDF_PAGEOBJ_PATH)
                    {
                        var rawPath = ExtractRawPathWithMetadata(pageObj, i);
                        if (rawPath != null && rawPath.Points.Count > 0)
                        {
                            AllPaths.Add(rawPath);
                        }
                    }
                }

                _logger.LogInformation("PDFium Native: Extracted {PathCount} paths", AllPaths.Count);

                // Apply filtering and gap bridging if settings provided
                List<RawPath> processedPaths = AllPaths;

                if (settings != null)
                {
                    _logger.LogInformation("PDFium Native: Applying settings - Length filter: {Enabled}, GapTolerance: {Tolerance}",
                        settings.Filters.Length.Enabled, settings.Polygon.GapTolerance);

                    // Apply all filters (includes statistics generation and smart filtering)
                    processedPaths = FilterPaths(processedPaths, settings, PageWidth, PageHeight);
                    _logger.LogInformation("PDFium Native: After filtering: {FilteredCount} paths (removed {RemovedCount})",
                        processedPaths.Count, AllPaths.Count - processedPaths.Count);

                    // Get raw filtered paths
                    FilteredPaths = processedPaths;

                    // Collapse parallel wall lines (for PDFs where walls have thickness)
                    // NOTE: For rooms with dividing walls, keeping both inner and outer lines may help
                    // polygon reconstruction find separate room boundaries. Set SkipCollapseParallelWalls=true to test.
                    if (settings.Polygon.WallThickness > 0)
                    {
                        if (!settings.Polygon.SkipCollapseParallelWalls)
                        {
                            processedPaths = _ntsPolygonizer.CollapseParallelWalls(processedPaths, settings.Polygon.WallThickness);
                            _logger.LogInformation("PDFium Native: After collapsing parallel walls: {Count} paths", processedPaths.Count);
                        }
                        else
                        {
                            _logger.LogInformation("PDFium Native: Skipping collapse parallel walls - keeping both inner and outer lines: {Count} paths", processedPaths.Count);
                            // Extract inner boundaries from double lines to help form separate room boundaries
                            processedPaths = _ntsPolygonizer.ExtractInnerBoundaries(processedPaths, settings.Polygon.WallThickness);
                            _logger.LogInformation("PDFium Native: After extracting inner boundaries: {Count} paths", processedPaths.Count);
                        }

                        // Merge collinear segments that are on the same line (within tolerance)
                        // This reduces multiple short segments on the same wall to a single longer segment
                        processedPaths = _ntsPolygonizer.MergeCollinearSegments(processedPaths, settings.Polygon.WallThickness / 2);
                        _logger.LogInformation("PDFium Native: After merging collinear segments: {Count} paths", processedPaths.Count);
                        AllPaths = processedPaths; // Update AllPaths for debugging/visualization
                        // After collapsing, extend lines to meet at intersection points
                        // Use wallThickness * 4 as snap tolerance to handle door gaps and wall offsets
                        processedPaths = _ntsPolygonizer.ExtendLinesToIntersections(processedPaths, settings.Polygon.WallThickness * 4);
                        _logger.LogInformation("PDFium Native: After extending to intersections: {Count} paths", processedPaths.Count);

                        // Filter out open lines AFTER extending
                        // These interfere with polygon reconstruction
                        processedPaths = _ntsPolygonizer.FilterOpenLines(processedPaths, settings.Polygon.WallThickness * 2);
                        _logger.LogInformation("PDFium Native: After filtering open lines: {Count} paths", processedPaths.Count);

                        // Duplicate internal dividing walls to create separate closed boundaries for adjacent rooms
                        // This allows the polygonizer to find multiple separate polygons instead of one large polygon
                        // NOTE: Skip this if we already handled it in ExtractInnerBoundaries
                        if (!settings.Polygon.SkipCollapseParallelWalls)
                        {
                            processedPaths = _ntsPolygonizer.DuplicateDividingWalls(processedPaths, settings.Polygon.WallThickness * 2);
                            _logger.LogInformation("PDFium Native: After duplicating dividing walls: {Count} paths", processedPaths.Count);
                        }
                        else
                        {
                            _logger.LogInformation("PDFium Native: Skipping DuplicateDividingWalls - already handled in ExtractInnerBoundaries");
                        }
                    }

                    // Apply gap bridging using NTS (much faster than custom implementation)
                    if (settings.Polygon.GapTolerance > 0)
                    {
                        processedPaths = _ntsPolygonizer.BridgeGaps(processedPaths, settings.Polygon.GapTolerance);
                        _logger.LogInformation("PDFium Native: After NTS gap bridging: {BridgedCount} paths", processedPaths.Count);
                    }

                   // AllPaths = processedPaths; // Update AllPaths for debugging/visualization
                }

                // Reconstruct closed polygons from paths using NTS Polygonizer (MUCH faster!)
                // NOTE: Order matters - we MUST reconstruct polygons BEFORE insetting because:
                // 1. ReconstructPolygons works on line segments (RawPath) to create polygons
                // 2. InsetPolygons works on polygons (RoomBoundary) to shrink them
                // 3. You cannot inset line segments - you need closed polygons first
                // If ReconstructPolygons only finds 1 polygon when there should be 2, the issue
                // is in the polygon reconstruction logic, not the order of operations.
                var roomBoundaries = _ntsPolygonizer.ReconstructPolygons(processedPaths);
                _logger.LogInformation("PDFium Native: NTS reconstructed {PolygonCount} polygons", roomBoundaries.Count);

                // Inset polygons by half wall thickness to get inner boundaries
                // This converts centerline-based polygons to proper inner room boundaries
                // Use the actual measured wall thickness from centerline paths, not the settings value
                if (settings?.Polygon.WallThickness > 0)
                {
                    double actualWallThickness = _ntsPolygonizer.CalculateWallThicknessForInset(processedPaths);
                    if (actualWallThickness > 0)
                    {
                        roomBoundaries = _ntsPolygonizer.InsetPolygons(roomBoundaries, actualWallThickness);
                        _logger.LogInformation("PDFium Native: After insetting by half wall thickness ({Thickness:F2}): {Count} polygons",
                            actualWallThickness, roomBoundaries.Count);
                    }
                    else
                    {
                        _logger.LogInformation("PDFium Native: No centerline paths found, skipping polygon inset");
                    }
                }

                // Store for visualization (convert back to List<Point2D> format)
                ClosedPolygons = roomBoundaries.Select(b => b.Polygon).ToList();

                // Remove outer polygons (keep inner boundaries per user request)
                if (settings?.Polygon.RemoveOuter == true)
                {
                    roomBoundaries = _ntsPolygonizer.RemoveOuterPolygons(roomBoundaries);
                    _logger.LogInformation("PDFium Native: After NTS removing outer polygons: {InnerCount} inner boundaries", roomBoundaries.Count);
                }

                // Remove nested polygons if requested
                if (settings?.Polygon.RemoveNested == true)
                {
                    roomBoundaries = _ntsPolygonizer.RemoveNestedPolygons(roomBoundaries);
                    _logger.LogInformation("PDFium Native: After NTS removing nested polygons: {Count} boundaries", roomBoundaries.Count);
                }

                // Filter by minimum area if specified
                if (settings?.Polygon.MinArea > 0)
                {
                    roomBoundaries = _ntsPolygonizer.FilterByMinArea(roomBoundaries, settings.Polygon.MinArea);
                    _logger.LogInformation("PDFium Native: After min area filter ({MinArea}): {Count} boundaries", settings.Polygon.MinArea, roomBoundaries.Count);
                }

                // Filter out thin strip polygons by minimum width
                // This removes polygons that form between parallel wall lines (wall thickness artifacts)
                if (settings?.Polygon.MinWidth > 0)
                {
                    roomBoundaries = _ntsPolygonizer.FilterByMinWidth(roomBoundaries, settings.Polygon.MinWidth);
                    _logger.LogInformation("PDFium Native: After min width filter ({MinWidth}): {Count} boundaries", settings.Polygon.MinWidth, roomBoundaries.Count);
                }

                // Add to boundaries list
                boundaries.AddRange(roomBoundaries);

                // Cleanup
                PDFiumNative.FPDF_ClosePage(page);
                PDFiumNative.FPDF_CloseDocument(document);
                PDFiumNative.FPDF_DestroyLibrary();

                _logger.LogInformation("PDFium Native: Extraction complete. Found {BoundaryCount} boundaries", boundaries.Count);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "PDFium Native: Error during boundary extraction");
                try
                {
                    PDFiumNative.FPDF_DestroyLibrary();
                }
                catch { }
            }

            return boundaries;
        }

        /// <summary>
        /// Extract path points from a PDFium path object
        /// NOTE: Each path object is typically a SINGLE LINE SEGMENT (2 points)
        /// We need to collect all segments and reconstruct closed polygons later
        /// </summary>
        private List<Point2D>? ExtractPathFromPDFiumObject(IntPtr pathObj)
        {
            try
            {
                // Get draw mode to check if this is a stroked/filled path
                if (!PDFiumNative.FPDFPath_GetDrawMode(pathObj, out int fillmode, out bool stroke))
                {
                    _logger.LogDebug("PDFium Native: Failed to get draw mode");
                    return null;
                }

                // Only process paths that are stroked (likely room boundaries)
                // Skip non-stroked paths (they might be fills or decorations)
                if (!stroke)
                {
                    _logger.LogDebug("PDFium Native: Skipping non-stroked path (fill={Fill})", fillmode);
                    return null;
                }

                int segmentCount = PDFiumNative.FPDFPath_CountSegments(pathObj);
                if (segmentCount == 0)
                    return null;

                var points = new List<Point2D>();

                for (int i = 0; i < segmentCount; i++)
                {
                    IntPtr segment = PDFiumNative.FPDFPath_GetPathSegment(pathObj, i);
                    if (segment == IntPtr.Zero)
                        continue;

                    if (PDFiumNative.FPDFPathSegment_GetPoint(segment, out float x, out float y))
                    {
                        points.Add(new Point2D { X = x, Y = y });
                    }
                }

                // Return all points from this path object (typically 2 points for a line segment)
                _logger.LogDebug("PDFium Native: Extracted path segment with {PointCount} points (stroke={Stroke}, fill={Fill})",
                    points.Count, stroke, fillmode);

                return points;
            }
            catch (Exception ex)
            {
                _logger.LogDebug("PDFium Native: Error extracting path: {Error}", ex.Message);
                return null;
            }
        }

        /// <summary>
        /// Extract text with positions using PDFium Native API
        /// This is much more direct than PdfPig for text extraction
        /// </summary>
        private List<RoomLabel> ExtractTextWithPDFiumNative(MemoryStream pdfStream)
        {
            var labels = new List<RoomLabel>();

            try
            {
                _logger.LogInformation("PDFium Native: Starting text extraction");

                // Initialize PDFium library
                PDFiumNative.FPDF_InitLibrary();

                // Load PDF from memory stream
                pdfStream.Position = 0;
                byte[] pdfBytes = pdfStream.ToArray();
                IntPtr document = PDFiumNative.FPDF_LoadMemDocument(pdfBytes, pdfBytes.Length, null);

                if (document == IntPtr.Zero)
                {
                    uint error = PDFiumNative.FPDF_GetLastError();
                    _logger.LogError("PDFium Native: Failed to load document. Error code: {ErrorCode}", error);
                    PDFiumNative.FPDF_DestroyLibrary();
                    return labels;
                }

                int pageCount = PDFiumNative.FPDF_GetPageCount(document);
                _logger.LogInformation("PDFium Native: Document has {PageCount} pages", pageCount);

                if (pageCount == 0)
                {
                    PDFiumNative.FPDF_CloseDocument(document);
                    PDFiumNative.FPDF_DestroyLibrary();
                    return labels;
                }

                // Process first page
                IntPtr page = PDFiumNative.FPDF_LoadPage(document, 0);
                if (page == IntPtr.Zero)
                {
                    _logger.LogError("PDFium Native: Failed to load page 0");
                    PDFiumNative.FPDF_CloseDocument(document);
                    PDFiumNative.FPDF_DestroyLibrary();
                    return labels;
                }

                // Load text page
                IntPtr textPage = PDFiumNative.FPDFText_LoadPage(page);
                if (textPage == IntPtr.Zero)
                {
                    _logger.LogError("PDFium Native: Failed to load text page");
                    PDFiumNative.FPDF_ClosePage(page);
                    PDFiumNative.FPDF_CloseDocument(document);
                    PDFiumNative.FPDF_DestroyLibrary();
                    return labels;
                }

                int charCount = PDFiumNative.FPDFText_CountChars(textPage);
                _logger.LogInformation("PDFium Native: Found {CharCount} characters", charCount);

                // Extract text by building words from characters
                var currentWord = new System.Text.StringBuilder();
                double wordLeft = 0, wordRight = 0, wordBottom = 0, wordTop = 0;
                bool inWord = false;

                for (int i = 0; i < charCount; i++)
                {
                    uint unicode = PDFiumNative.FPDFText_GetUnicode(textPage, i);
                    char ch = (char)unicode;

                    if (PDFiumNative.FPDFText_GetCharBox(textPage, i, out double left, out double right, out double bottom, out double top))
                    {
                        if (char.IsLetterOrDigit(ch) || ch == '_' || ch == '-')
                        {
                            if (!inWord)
                            {
                                // Start new word
                                wordLeft = left;
                                wordBottom = bottom;
                                inWord = true;
                            }
                            currentWord.Append(ch);
                            wordRight = right;
                            wordTop = top;
                        }
                        else
                        {
                            // End of word
                            if (inWord && currentWord.Length > 0)
                            {
                                labels.Add(new RoomLabel
                                {
                                    Text = currentWord.ToString(),
                                    CenterX = (wordLeft + wordRight) / 2,
                                    CenterY = (wordBottom + wordTop) / 2
                                });
                                currentWord.Clear();
                                inWord = false;
                            }
                        }
                    }
                }

                // Add last word if any
                if (inWord && currentWord.Length > 0)
                {
                    labels.Add(new RoomLabel
                    {
                        Text = currentWord.ToString(),
                        CenterX = (wordLeft + wordRight) / 2,
                        CenterY = (wordBottom + wordTop) / 2
                    });
                }

                // Cleanup
                PDFiumNative.FPDFText_ClosePage(textPage);
                PDFiumNative.FPDF_ClosePage(page);
                PDFiumNative.FPDF_CloseDocument(document);
                PDFiumNative.FPDF_DestroyLibrary();

                _logger.LogInformation("PDFium Native: Text extraction complete. Found {LabelCount} text labels", labels.Count);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "PDFium Native: Error during text extraction");
                try
                {
                    PDFiumNative.FPDF_DestroyLibrary();
                }
                catch { }
            }

            return labels;
        }

        /// <summary>
        /// Extract room boundaries using PdfPig's page operations (PDF content stream parsing)
        /// Parses PDF operators to reconstruct vector paths and identify closed polygons
        /// </summary>
        private List<RoomBoundary> ExtractRoomBoundariesWithPdfPig(MemoryStream pdfStream)
        {
            var boundaries = new List<RoomBoundary>();

            try
            {
                pdfStream.Position = 0;

                _logger.LogDebug("PdfPig: Extracting vector paths from PDF operations");

                using (var document = PdfPig.PdfDocument.Open(pdfStream))
                {
                    if (document.NumberOfPages == 0)
                    {
                        _logger.LogWarning("PDF has no pages");
                        return boundaries;
                    }

                    var page = document.GetPage(1);
                    var operations = page.Operations;

                    _logger.LogDebug("PdfPig: Found {OperationCount} page operations", operations.Count);

                    // Parse operations to extract paths
                    // Track line width to filter out thin lines (decorations, text underlines, etc.)
                    var currentPath = new List<Point2D>();
                    var allPaths = new List<List<Point2D>>();
                    double currentLineWidth = 0.0;
                    const double minLineWidthForWalls = 0.5; // Minimum line width to consider as wall

                    foreach (var op in operations)
                    {
                        var opType = op.GetType().Name;

                        switch (opType)
                        {
                            case "SetLineWidth": // w operator
                                // Track current line width
                                var widthProp = op.GetType().GetProperty("Width");
                                if (widthProp != null)
                                {
                                    currentLineWidth = Convert.ToDouble(widthProp.GetValue(op));
                                    _logger.LogDebug("Line width set to: {Width}", currentLineWidth);
                                }
                                break;

                            case "BeginNewSubpath": // m operator
                                // Start a new path
                                if (currentPath.Count > 0)
                                {
                                    allPaths.Add(new List<Point2D>(currentPath));
                                    currentPath.Clear();
                                }

                                // Get X and Y from the operation
                                var xProp = op.GetType().GetProperty("X");
                                var yProp = op.GetType().GetProperty("Y");
                                if (xProp != null && yProp != null)
                                {
                                    var x = Convert.ToDouble(xProp.GetValue(op));
                                    var y = Convert.ToDouble(yProp.GetValue(op));
                                    currentPath.Add(new Point2D { X = x, Y = y });
                                }
                                break;

                            case "AppendStraightLineSegment": // l operator
                                // Add line endpoint to current path
                                xProp = op.GetType().GetProperty("X");
                                yProp = op.GetType().GetProperty("Y");
                                if (xProp != null && yProp != null)
                                {
                                    var x = Convert.ToDouble(xProp.GetValue(op));
                                    var y = Convert.ToDouble(yProp.GetValue(op));
                                    currentPath.Add(new Point2D { X = x, Y = y });
                                }
                                break;

                            case "CloseSubpath": // h operator
                                // Close the path (connect last point to first)
                                if (currentPath.Count > 0 && currentLineWidth >= minLineWidthForWalls)
                                {
                                    // Path is now closed and has sufficient line width
                                    allPaths.Add(new List<Point2D>(currentPath));
                                    _logger.LogDebug("Saved closed path with {PointCount} points (line width: {Width})", currentPath.Count, currentLineWidth);
                                    currentPath.Clear();
                                }
                                else if (currentPath.Count > 0)
                                {
                                    _logger.LogDebug("Skipped thin closed path (line width: {Width} < {MinWidth})", currentLineWidth, minLineWidthForWalls);
                                    currentPath.Clear();
                                }
                                break;

                            case "StrokePath": // S operator
                            case "FillPathNonZeroWinding": // f operator
                            case "FillPathEvenOdd": // f* operator
                                // Path painting operator - save current path if it has points and sufficient line width
                                if (currentPath.Count > 0 && currentLineWidth >= minLineWidthForWalls)
                                {
                                    allPaths.Add(new List<Point2D>(currentPath));
                                    _logger.LogDebug("Saved stroked/filled path with {PointCount} points (line width: {Width})", currentPath.Count, currentLineWidth);
                                    currentPath.Clear();
                                }
                                else if (currentPath.Count > 0)
                                {
                                    _logger.LogDebug("Skipped thin stroked/filled path (line width: {Width} < {MinWidth})", currentLineWidth, minLineWidthForWalls);
                                    currentPath.Clear();
                                }
                                break;
                        }
                    }

                    // Add any remaining path (if it has sufficient line width)
                    if (currentPath.Count > 0 && currentLineWidth >= minLineWidthForWalls)
                    {
                        allPaths.Add(currentPath);
                        _logger.LogDebug("Saved remaining path with {PointCount} points (line width: {Width})", currentPath.Count, currentLineWidth);
                    }

                    _logger.LogDebug("PdfPig: Extracted {PathCount} paths from operations", allPaths.Count);

                    // Reconstruct closed polygons from line segments
                    // Many PDFs draw shapes as separate line segments that need to be connected
                    var closedPolygons = ReconstructClosedPolygons(allPaths);

                    _logger.LogInformation("PdfPig: Reconstructed {PolygonCount} closed polygons", closedPolygons.Count);

                    // Log detailed coordinates for each polygon (Debug level to reduce verbosity)
                    for (int i = 0; i < closedPolygons.Count; i++)
                    {
                        var polygon = closedPolygons[i];
                        _logger.LogDebug("Polygon {Index}: {PointCount} points", i, polygon.Count);
                        for (int j = 0; j < polygon.Count; j++)
                        {
                            _logger.LogDebug("  Point[{PointIndex}]: X={X:F2}, Y={Y:F2}", j, polygon[j].X, polygon[j].Y);
                        }
                    }

                    // Remove nested polygons (walls have thickness, so we get inner/outer boundaries)
                    //var uniquePolygons = RemoveNestedPolygons(closedPolygons);

                    //_logger.LogInformation("PdfPig: After removing nested polygons: {UniqueCount} unique boundaries", uniquePolygons.Count);

                    // Filter for room-sized polygons
                    foreach (var polygon in closedPolygons)
                    {
                        if (polygon.Count >= 4)
                        {
                            boundaries.Add(new RoomBoundary { Polygon = polygon });
                        }
                    }

                    _logger.LogDebug("PdfPig: Found {BoundaryCount} potential room boundaries", boundaries.Count);
                }

                _logger.LogDebug("PdfPig: Extracted {BoundaryCount} boundaries", boundaries.Count);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in PdfPig boundary extraction");
            }

            // If no boundaries found, create sample boundaries for testing
            if (boundaries.Count == 0)
            {
                _logger.LogWarning("No boundaries extracted, creating sample boundaries for testing");               
            }

            return boundaries;
        }

        /// <summary>
        /// Match room labels with boundaries based on proximity
        /// </summary>
        private List<RoomModel> MatchLabelsWithBoundaries(List<RoomLabel> labels, List<RoomBoundary> boundaries)
        {
            var rooms = new List<RoomModel>();
            int roomIndex = 0;

            // Simple matching: for each boundary, find the closest label
            foreach (var boundary in boundaries)
            {
                roomIndex++;
                var centroid = CalculateCentroid(boundary.Polygon);

                // Find closest label
                RoomLabel? closestLabel = null;
                double minDistance = double.MaxValue;

                foreach (var label in labels)
                {
                    double distance = Math.Sqrt(
                        Math.Pow(label.CenterX - centroid.X, 2) +
                        Math.Pow(label.CenterY - centroid.Y, 2)
                    );

                    if (distance < minDistance)
                    {
                        minDistance = distance;
                        closestLabel = label;
                    }
                }

                rooms.Add(new RoomModel
                {
                    Id = $"room-{roomIndex:D3}",
                    Name = closestLabel?.Text ?? $"Room {roomIndex}",
                    Polygon = boundary.Polygon
                });
            }

            return rooms;
        }

        /// <summary>
        /// Reconstruct closed polygons from line segments
        /// Connects line segments that share endpoints to form closed shapes
        /// </summary>
        private List<List<Point2D>> ReconstructClosedPolygons(List<List<Point2D>> segments)
        {
            var closedPolygons = new List<List<Point2D>>();
            var usedSegments = new HashSet<int>();
            const double tolerance = 0.1; // Tolerance for point matching

            // Try to build closed polygons by connecting segments
            for (int i = 0; i < segments.Count; i++)
            {
                if (usedSegments.Contains(i))
                    continue;

                var segment = segments[i];
                if (segment.Count < 2)
                    continue;

                // Start a new polygon with this segment
                var polygon = new List<Point2D>(segment);
                usedSegments.Add(i);

                // Try to extend the polygon by finding connecting segments
                bool foundConnection = true;
                int maxIterations = segments.Count; // Prevent infinite loops
                int iterations = 0;

                while (foundConnection && iterations < maxIterations)
                {
                    foundConnection = false;
                    iterations++;

                    var lastPoint = polygon[polygon.Count - 1];

                    // Look for a segment that starts where this polygon ends
                    for (int j = 0; j < segments.Count; j++)
                    {
                        if (usedSegments.Contains(j))
                            continue;

                        var nextSegment = segments[j];
                        if (nextSegment.Count < 2)
                            continue;

                        var nextStart = nextSegment[0];
                        var nextEnd = nextSegment[nextSegment.Count - 1];

                        // Check if this segment connects to the end of our polygon
                        if (ArePointsClose(lastPoint, nextStart, tolerance))
                        {
                            // Add all points except the first (which is duplicate)
                            for (int k = 1; k < nextSegment.Count; k++)
                            {
                                polygon.Add(nextSegment[k]);
                            }
                            usedSegments.Add(j);
                            foundConnection = true;
                            break;
                        }
                        // Check if segment is reversed
                        else if (ArePointsClose(lastPoint, nextEnd, tolerance))
                        {
                            // Add points in reverse order, except the last (which is duplicate)
                            for (int k = nextSegment.Count - 2; k >= 0; k--)
                            {
                                polygon.Add(nextSegment[k]);
                            }
                            usedSegments.Add(j);
                            foundConnection = true;
                            break;
                        }
                    }
                }

                // Check if polygon is closed (last point connects to first)
                if (polygon.Count >= 4)
                {
                    var firstPoint = polygon[0];
                    var lastPoint = polygon[polygon.Count - 1];

                    if (ArePointsClose(firstPoint, lastPoint, tolerance))
                    {
                        // Remove duplicate last point
                        polygon.RemoveAt(polygon.Count - 1);
                        closedPolygons.Add(polygon);
                        _logger.LogDebug("Found closed polygon with {PointCount} points", polygon.Count);
                    }
                }
            }

            return closedPolygons;
        }

        /// <summary>
        /// Remove nested polygons (e.g., inner and outer wall boundaries)
        /// Keeps only the outer polygon when two polygons are nested with similar shape
        /// </summary>
        private List<List<Point2D>> RemoveNestedPolygons(List<List<Point2D>> polygons)
        {
            if (polygons.Count <= 1)
                return polygons;

            var result = new List<List<Point2D>>();
            var toRemove = new HashSet<int>();

            // Check each pair of polygons
            for (int i = 0; i < polygons.Count; i++)
            {
                if (toRemove.Contains(i))
                    continue;

                for (int j = i + 1; j < polygons.Count; j++)
                {
                    if (toRemove.Contains(j))
                        continue;

                    var poly1 = polygons[i];
                    var poly2 = polygons[j];

                    // Check if one polygon is nested inside the other
                    if (IsPolygonNested(poly1, poly2, out bool poly1IsInner))
                    {
                        // Remove the inner polygon, keep the outer one
                        if (poly1IsInner)
                        {
                            toRemove.Add(i);
                            _logger.LogDebug("Removing nested polygon {Index} (inner) - keeping polygon {OuterIndex} (outer)", i, j);
                            break; // poly1 is removed, no need to check further
                        }
                        else
                        {
                            toRemove.Add(j);
                            _logger.LogDebug("Removing nested polygon {Index} (inner) - keeping polygon {OuterIndex} (outer)", j, i);
                        }
                    }
                }
            }

            // Add polygons that weren't removed
            for (int i = 0; i < polygons.Count; i++)
            {
                if (!toRemove.Contains(i))
                {
                    result.Add(polygons[i]);
                }
            }

            return result;
        }

        /// <summary>
        /// Check if two polygons are nested (one inside the other)
        /// Returns true if they are nested, and sets isFirstInner to indicate which is inner
        /// </summary>
        private bool IsPolygonNested(List<Point2D> poly1, List<Point2D> poly2, out bool isFirstInner)
        {
            isFirstInner = false;

            // Check if poly1 is inside poly2
            var centroid1 = CalculateCentroid(poly1);
            var centroid2 = CalculateCentroid(poly2);

            // If centroids are very close, these might be nested polygons
            var centroidDistance = Math.Sqrt(
                Math.Pow(centroid1.X - centroid2.X, 2) +
                Math.Pow(centroid1.Y - centroid2.Y, 2)
            );

            // If centroids are far apart, they're not nested
            if (centroidDistance > 50.0)
                return false;

            // Check if all points of poly1 are inside poly2 (or vice versa)
            bool allPoly1InsidePoly2 = poly1.All(p => IsPointInPolygon(p, poly2));
            bool allPoly2InsidePoly1 = poly2.All(p => IsPointInPolygon(p, poly1));

            if (allPoly1InsidePoly2)
            {
                isFirstInner = true;
                return true;
            }
            else if (allPoly2InsidePoly1)
            {
                isFirstInner = false;
                return true;
            }

            return false;
        }

        /// <summary>
        /// Check if a point is inside a polygon using ray casting algorithm
        /// </summary>
        private bool IsPointInPolygon(Point2D point, List<Point2D> polygon)
        {
            if (polygon.Count < 3)
                return false;

            bool inside = false;
            int j = polygon.Count - 1;

            for (int i = 0; i < polygon.Count; i++)
            {
                if ((polygon[i].Y > point.Y) != (polygon[j].Y > point.Y) &&
                    point.X < (polygon[j].X - polygon[i].X) * (point.Y - polygon[i].Y) /
                    (polygon[j].Y - polygon[i].Y) + polygon[i].X)
                {
                    inside = !inside;
                }
                j = i;
            }

            return inside;
        }

        /// <summary>
        /// Check if two points are close within tolerance
        /// </summary>
        private bool ArePointsClose(Point2D p1, Point2D p2, double tolerance)
        {
            var distance = Math.Sqrt(Math.Pow(p1.X - p2.X, 2) + Math.Pow(p1.Y - p2.Y, 2));
            return distance <= tolerance;
        }

        /// <summary>
        /// Calculate centroid of a polygon
        /// </summary>
        private Point2D CalculateCentroid(List<Point2D> polygon)
        {
            if (polygon.Count == 0)
                return new Point2D { X = 0, Y = 0 };

            double sumX = 0, sumY = 0;
            foreach (var point in polygon)
            {
                sumX += point.X;
                sumY += point.Y;
            }

            return new Point2D
            {
                X = sumX / polygon.Count,
                Y = sumY / polygon.Count
            };
        }

        #region Path Extraction Helpers

        /// <summary>
        /// Extract a raw path with all metadata
        /// Used internally during extraction to populate AllPaths property
        /// </summary>
        private RawPath? ExtractRawPathWithMetadata(IntPtr pathObj, int objectIndex)
        {
            try
            {
                var rawPath = new RawPath { ObjectIndex = objectIndex };

                // Get draw mode
                if (PDFiumNative.FPDFPath_GetDrawMode(pathObj, out int fillmode, out bool stroke))
                {
                    rawPath.IsStroked = stroke;
                    rawPath.IsFilled = fillmode != 0;
                }

                // Get line width
                if (PDFiumNative.FPDFPageObj_GetStrokeWidth(pathObj, out float lineWidth))
                {
                    rawPath.LineWidth = lineWidth;
                }

                // Get segment count
                int segmentCount = PDFiumNative.FPDFPath_CountSegments(pathObj);
                rawPath.SegmentCount = segmentCount;

                // Extract all points
                var points = new List<Point2D>();
                for (int i = 0; i < segmentCount; i++)
                {
                    IntPtr segment = PDFiumNative.FPDFPath_GetPathSegment(pathObj, i);
                    if (segment != IntPtr.Zero)
                    {
                        if (PDFiumNative.FPDFPathSegment_GetPoint(segment, out float x, out float y))
                        {
                            points.Add(new Point2D { X = x, Y = y });
                        }
                    }
                }

                rawPath.Points = points;

                // Calculate path length
                rawPath.PathLength = CalculatePathLength(points);

                // Determine path type
                rawPath.PathType = DeterminePathType(points, segmentCount);
                rawPath.PathTypeEnum = PathTypeEnum.Original; // All paths from PDF are original
                rawPath.WallThickness = 0; // No wall thickness for original paths

                return rawPath;
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Error extracting raw path metadata for object {Index}", objectIndex);
                return null;
            }
        }

        /// <summary>
        /// Calculate total length of a path
        /// </summary>
        private double CalculatePathLength(List<Point2D> points)
        {
            if (points.Count < 2)
                return 0;

            double totalLength = 0;
            for (int i = 0; i < points.Count - 1; i++)
            {
                double dx = points[i + 1].X - points[i].X;
                double dy = points[i + 1].Y - points[i].Y;
                totalLength += Math.Sqrt(dx * dx + dy * dy);
            }

            return totalLength;
        }

        /// <summary>
        /// Determine the type of path based on points and segments
        /// </summary>
        private string DeterminePathType(List<Point2D> points, int segmentCount)
        {
            if (points.Count == 0)
                return "Empty";
            if (points.Count == 1)
                return "Point";
            if (points.Count == 2)
                return "Line";
            if (points.Count == 4 || points.Count == 5)
            {
                // Check if it's a rectangle
                if (IsRectangle(points))
                    return "Rectangle";
            }
            if (segmentCount > points.Count)
                return "Curve";

            return "Polyline";
        }

        /// <summary>
        /// Check if points form a rectangle
        /// </summary>
        private bool IsRectangle(List<Point2D> points)
        {
            if (points.Count < 4)
                return false;

            // Simple check: 4 points with right angles
            // More sophisticated check could be added
            return true; // Simplified for now
        }

        #endregion

        #region Settings and Filtering

        // NOTE: The filtering and statistics features are intended to support later statistics
        // analysis and ML (machine learning) analysis. The path statistics, line width distributions,
        // and filtering capabilities are designed to enable data-driven improvements to extraction
        // algorithms and quality assessment.

        /// <summary>
        /// Analyze path distributions and generate statistics
        /// </summary>
        /// <param name="paths">List of paths to analyze</param>
        /// <param name="pageWidth">Page width for room size calculations</param>
        /// <param name="pageHeight">Page height for room size calculations</param>
        /// <returns>PathStatistics object with distribution data</returns>
        public PathStatistics GetStatistics(List<RawPath> paths, double pageWidth, double pageHeight)
        {
            var stats = new PathStatistics
            {
                TotalPaths = paths.Count,
                PageWidth = pageWidth,
                PageHeight = pageHeight
            };

            if (paths.Count == 0)
            {
                _logger.LogWarning("No paths to analyze for statistics");
                return stats;
            }

            // Calculate basic statistics
            stats.MinLength = paths.Min(p => p.PathLength);
            stats.MaxLength = paths.Max(p => p.PathLength);
            stats.AverageLength = paths.Average(p => p.PathLength);
            stats.MinLineWidth = paths.Min(p => p.LineWidth);
            stats.MaxLineWidth = paths.Max(p => p.LineWidth);
            stats.AverageLineWidth = paths.Average(p => p.LineWidth);

            // Build line width distribution
            foreach (var path in paths)
            {
                double width = Math.Round(path.LineWidth, 2); // Round to 2 decimal places
                if (!stats.LineWidthDistribution.ContainsKey(width))
                {
                    stats.LineWidthDistribution[width] = new List<RawPath>();
                }
                stats.LineWidthDistribution[width].Add(path);
            }

            // Sort line widths by thickness (descending - thickest first)
            stats.SortedLineWidths = stats.LineWidthDistribution.Keys
                .OrderByDescending(w => w)
                .ToList();

            // Build length range distribution
            // Divide (max - min) by 20 to create buckets
            double lengthRange = stats.MaxLength - stats.MinLength;

            // Handle edge case where all paths have the same length
            if (lengthRange < 0.01)
            {
                // All paths have essentially the same length - create a single bucket
                string rangeKey = $"{stats.MinLength:F1}-{stats.MaxLength:F1}";
                stats.LengthRangeDistribution[rangeKey] = new List<RawPath>(paths);
                stats.SortedLengthRanges = new List<string> { rangeKey };
            }
            else
            {
                int bucketCount = 20;
                double bucketSize = lengthRange / bucketCount;

                if (bucketSize < 1)
                    bucketSize = 1; // Minimum bucket size

                foreach (var path in paths)
                {
                    int bucketIndex = (int)((path.PathLength - stats.MinLength) / bucketSize);
                    if (bucketIndex >= bucketCount)
                        bucketIndex = bucketCount - 1; // Last bucket includes max value

                    double rangeStart = stats.MinLength + (bucketIndex * bucketSize);
                    double rangeEnd = rangeStart + bucketSize;
                    string rangeKey = $"{rangeStart:F1}-{rangeEnd:F1}";

                    if (!stats.LengthRangeDistribution.ContainsKey(rangeKey))
                    {
                        stats.LengthRangeDistribution[rangeKey] = new List<RawPath>();
                    }
                    stats.LengthRangeDistribution[rangeKey].Add(path);
                }

                // Sort length ranges by start value
                stats.SortedLengthRanges = stats.LengthRangeDistribution.Keys
                    .OrderBy(k => double.Parse(k.Split('-')[0]))
                    .ToList();
            }

            // Build layer distribution (if layer info is available)
            foreach (var path in paths)
            {
                string layer = path.Layer ?? "Unknown";
                if (!stats.LayerDistribution.ContainsKey(layer))
                {
                    stats.LayerDistribution[layer] = new List<RawPath>();
                }
                stats.LayerDistribution[layer].Add(path);
            }

            // Log statistics summary
            _logger.LogInformation("=== PATH STATISTICS ===");
            _logger.LogInformation("Total paths: {Count}", stats.TotalPaths);
            _logger.LogInformation("Length: Min={Min:F2}, Max={Max:F2}, Avg={Avg:F2}",
                stats.MinLength, stats.MaxLength, stats.AverageLength);
            _logger.LogInformation("Line Width: Min={Min:F2}, Max={Max:F2}, Avg={Avg:F2}",
                stats.MinLineWidth, stats.MaxLineWidth, stats.AverageLineWidth);
            _logger.LogInformation("Line Width Distribution: {Count} unique widths",
                stats.LineWidthDistribution.Count);
            foreach (var width in stats.SortedLineWidths.Take(5)) // Top 5 thickest
            {
                _logger.LogInformation("  Width {Width:F2}: {Count} paths",
                    width, stats.LineWidthDistribution[width].Count);
            }
            _logger.LogInformation("Length Range Distribution: {Count} buckets",
                stats.LengthRangeDistribution.Count);
            foreach (var range in stats.SortedLengthRanges.Take(5)) // First 5 ranges
            {
                _logger.LogInformation("  Range {Range}: {Count} paths",
                    range, stats.LengthRangeDistribution[range].Count);
            }
            if (stats.LayerDistribution.Count > 1 || !stats.LayerDistribution.ContainsKey("Unknown"))
            {
                _logger.LogInformation("Layer Distribution: {Count} layers",
                    stats.LayerDistribution.Count);
                foreach (var layer in stats.LayerDistribution.Keys)
                {
                    _logger.LogInformation("  Layer '{Layer}': {Count} paths",
                        layer, stats.LayerDistribution[layer].Count);
                }
            }
            _logger.LogInformation("=== END STATISTICS ===");

            return stats;
        }

        /// <summary>
        /// Load extraction settings collection from JSON file
        /// </summary>
        public ExtractionSettingsCollection LoadSettingsCollection(string settingsPath)
        {
            var manager = ExtractionSettingsManager.Instance;
            return manager.GetCollection(settingsPath);
        }

        /// <summary>
        /// Load extraction settings from JSON file (legacy method - loads default settings only)
        /// </summary>
        public ExtractionSettings LoadSettings(string settingsPath)
        {
            var manager = ExtractionSettingsManager.Instance;
            var collection = manager.GetCollection(settingsPath);
            return collection.Default;
        }

        /// <summary>
        /// Load extraction settings for a specific PDF file
        /// </summary>
        /// <param name="settingsPath">Path to the settings JSON file. If null or empty, defaults to "ExtractorSettings/extraction-settings.json" in the base directory.</param>
        /// <param name="pdfFileName">PDF filename (e.g., "Project3_onlywall.pdf")</param>
        /// <returns>Settings for the PDF or default settings</returns>
        public ExtractionSettings LoadSettingsForPdf(string pdfFileName)
        {
            var manager = ExtractionSettingsManager.Instance;
            var settings = manager.GetSettingsForPdf(pdfFileName);

            // Check if PDF-specific settings were found (check both full name and filename only)
            // Dictionary is case-insensitive, so no need to normalize
            var collection = manager.GetCollection(); // Use default settings path
            var fileNameOnly = Path.GetFileName(pdfFileName);
            bool hasPdfSpecificSettings = collection.PdfTypes.ContainsKey(pdfFileName) ||
                                         collection.PdfTypes.ContainsKey(fileNameOnly);

            _logger.LogInformation("Loaded settings for PDF '{PdfName}': Using {SettingsType} settings",
                pdfFileName,
                hasPdfSpecificSettings ? "PDF-specific" : "default");

            return settings;
        }

        /// <summary>
        /// Save extraction settings collection to JSON file
        /// </summary>
        public void SaveSettingsCollection(string settingsPath, ExtractionSettingsCollection settingsCollection)
        {
            try
            {
                var options = new JsonSerializerOptions
                {
                    WriteIndented = true,
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                };

                string json = JsonSerializer.Serialize(settingsCollection, options);
                File.WriteAllText(settingsPath, json);

                _logger.LogInformation("Saved extraction settings collection to {Path}", settingsPath);
                _logger.LogInformation("Saved {Count} PDF-specific configurations", settingsCollection.PdfTypes.Count);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error saving settings to {Path}", settingsPath);
                throw;
            }
        }

        /// <summary>
        /// Save or update settings for a specific PDF file
        /// </summary>
        /// <param name="settingsPath">Path to the settings JSON file</param>
        /// <param name="pdfFileName">PDF filename (e.g., "Project3_onlywall.pdf")</param>
        /// <param name="settings">Settings to save for this PDF</param>
        public void SaveSettingsForPdf(string settingsPath, string pdfFileName, ExtractionSettings settings)
        {
            try
            {
                var manager = ExtractionSettingsManager.Instance;
                
                // Load existing collection or create new one
                var collection = File.Exists(settingsPath)
                    ? manager.GetCollection(settingsPath)
                    : new ExtractionSettingsCollection();

                // Dictionary is case-insensitive, so no need to normalize
                // Update or add settings for this PDF
                collection.PdfTypes[pdfFileName] = settings;

                // Save the updated collection
                SaveSettingsCollection(settingsPath, collection);

                // Clear cache so next access will reload
                manager.ClearCache();

                _logger.LogInformation("Saved settings for PDF '{PdfName}' to {Path}", pdfFileName, settingsPath);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error saving settings for PDF '{PdfName}' to {Path}", pdfFileName, settingsPath);
                throw;
            }
        }

        /// <summary>
        /// Filter paths by room size based on page dimensions and expected room count
        /// </summary>
        /// <param name="paths">List of paths to filter</param>
        /// <param name="settings">Room size settings</param>
        /// <param name="pageWidth">Page width</param>
        /// <param name="pageHeight">Page height</param>
        /// <returns>Filtered list of paths</returns>
        public List<RawPath> FilterByRoomSize(List<RawPath> paths, RoomSizeSettings settings, double pageWidth, double pageHeight)
        {
            if (!settings.Enabled || paths.Count == 0)
                return paths;

            // Calculate expected room size range based on page dimensions
            // Min room size: page dimension / max room count
            // Max room size: page dimension / min room count
            double minExpectedRoomWidth = pageWidth / settings.MaxRoomCount;
            double minExpectedRoomHeight = pageHeight / settings.MaxRoomCount;
            double maxExpectedRoomWidth = pageWidth / settings.MinRoomCount;
            double maxExpectedRoomHeight = pageHeight / settings.MinRoomCount;

            // Use the smaller dimension for min threshold, larger for max threshold
            double minThreshold = Math.Min(minExpectedRoomWidth, minExpectedRoomHeight);
            double maxThreshold = Math.Max(maxExpectedRoomWidth, maxExpectedRoomHeight);

            _logger.LogInformation("Room size filter: Min={Min:F2}, Max={Max:F2} (based on page {W}x{H}, rooms {MinR}-{MaxR})",
                minThreshold, maxThreshold, pageWidth, pageHeight, settings.MinRoomCount, settings.MaxRoomCount);

            var filtered = paths.Where(p =>
                p.PathLength >= minThreshold &&
                p.PathLength <= maxThreshold).ToList();

            _logger.LogInformation("Room size filter: {Original} → {Filtered} paths",
                paths.Count, filtered.Count);

            return filtered;
        }

        /// <summary>
        /// Filter paths by line width based on statistics - keep only specific range of line widths
        /// </summary>
        /// <param name="paths">List of paths to filter</param>
        /// <param name="stats">Path statistics</param>
        /// <param name="topN">Number of line widths to keep (default: 3)</param>
        /// <param name="skipN">Number of thickest line widths to skip before taking topN (default: 0 for thickest, 3 for 2nd tier, etc.)</param>
        /// <returns>Filtered list of paths</returns>
        public List<RawPath> FilterByLineWidth(List<RawPath> paths, PathStatistics stats, int topN = 3, int skipN = 4)
        {
            if (paths.Count == 0 || stats.SortedLineWidths.Count == 0)
                return paths;

            // Get the line widths, skipping the first skipN and taking the next topN
            var selectedWidths = stats.SortedLineWidths.Skip(skipN).Take(topN).ToHashSet();

            if (skipN > 0)
            {
                _logger.LogInformation("Line width filter: Skipping top {Skip}, keeping next {N} widths: {Widths}",
                    skipN, topN, string.Join(", ", selectedWidths.Select(w => w.ToString("F2"))));
            }
            else
            {
                _logger.LogInformation("Line width filter: Keeping top {N} thickest widths: {Widths}",
                    topN, string.Join(", ", selectedWidths.Select(w => w.ToString("F2"))));
            }

            var filtered = paths.Where(p =>
            {
                double roundedWidth = Math.Round(p.LineWidth, 2);
                return selectedWidths.Contains(roundedWidth);
            }).ToList();

            _logger.LogInformation("Line width filter: {Original} → {Filtered} paths",
                paths.Count, filtered.Count);

            return filtered;
        }

        /// <summary>
        /// Filter paths by specific line width values - keep only paths with line widths in the specified list
        /// </summary>
        /// <param name="paths">List of paths to filter</param>
        /// <param name="lineWidths">List of specific line width values to keep (e.g., [1.0, 2.5, 5.0])</param>
        /// <returns>Filtered list of paths</returns>
        public List<RawPath> FilterByLineWidth(List<RawPath> paths, List<double> lineWidths)
        {
            if (paths.Count == 0 || lineWidths.Count == 0)
                return paths;

            // Round the specified widths to 2 decimal places for consistent comparison
            var targetWidths = lineWidths.Select(w => Math.Round(w, 2)).ToHashSet();

            _logger.LogInformation("Line width filter: Keeping paths with specific widths: {Widths}",
                string.Join(", ", targetWidths.Select(w => w.ToString("F2"))));

            var filtered = paths.Where(p =>
            {
                double roundedWidth = Math.Round(p.LineWidth, 2);
                return targetWidths.Contains(roundedWidth);
            }).ToList();

            _logger.LogInformation("Line width filter: {Original} → {Filtered} paths",
                paths.Count, filtered.Count);

            return filtered;
        }

        /// <summary>
        /// Filter paths by line width range (min/max)
        /// </summary>
        /// <param name="paths">List of paths to filter</param>
        /// <param name="minWidth">Minimum line width to keep</param>
        /// <param name="maxWidth">Maximum line width to keep</param>
        /// <returns>Filtered list of paths</returns>
        public List<RawPath> FilterByLineWidth(List<RawPath> paths, double minWidth, double maxWidth)
        {
            if (paths.Count == 0)
                return paths;

            _logger.LogInformation("Line width filter: Keeping paths with width range [{Min}, {Max}]",
                minWidth.ToString("F2"), maxWidth.ToString("F2"));

            var filtered = paths.Where(p => p.LineWidth >= minWidth && p.LineWidth <= maxWidth).ToList();

            _logger.LogInformation("Line width filter: {Original} → {Filtered} paths",
                paths.Count, filtered.Count);

            return filtered;
        }

        /// <summary>
        /// Filter paths based on extraction settings
        /// </summary>
        /// <param name="paths">List of paths to filter</param>
        /// <param name="settings">Extraction settings</param>
        /// <param name="pageWidth">Page width for room size calculations</param>
        /// <param name="pageHeight">Page height for room size calculations</param>
        /// <returns>Filtered list of paths</returns>
        public List<RawPath> FilterPaths(List<RawPath> paths, ExtractionSettings settings, double pageWidth, double pageHeight)
        {
            if (paths.Count == 0)
                return paths;

            int originalCount = paths.Count;
            var filtered = paths;

            // Generate statistics for all paths (needed for smart filtering)
            var statistics = GetStatistics(paths, pageWidth, pageHeight);

            // Apply line width filter if enabled (using min/max range from settings)
            if (settings.Filters.LineWidth.Enabled)
            {
                filtered = FilterByLineWidth(filtered, settings.Filters.LineWidth.MinWidth, settings.Filters.LineWidth.MaxWidth);
                _logger.LogDebug("Applied line width filter (range [{Min}, {Max}]): {Original} → {Filtered}",
                    settings.Filters.LineWidth.MinWidth, settings.Filters.LineWidth.MaxWidth, originalCount, filtered.Count);
            }

            // Apply room size filter if enabled
            if (settings.RoomSize.Enabled)
            {
                filtered = FilterByRoomSize(filtered, settings.RoomSize, pageWidth, pageHeight);
                _logger.LogDebug("Applied room size filter: {Original} → {Filtered}",
                    originalCount, filtered.Count);
            }

            // Apply length filter
            if (settings.Filters.Length.Enabled)
            {
                filtered = filtered.Where(p => p.PathLength >= settings.Filters.Length.MinLength).ToList();
                _logger.LogDebug("Applied length filter (min {MinLength}): {Original} → {Filtered}",
                    settings.Filters.Length.MinLength, originalCount, filtered.Count);
            }

            // Apply shape filter
            if (settings.Filters.Shape.Enabled && settings.Filters.Shape.AllowedShapes.Count > 0)
            {
                filtered = filtered.Where(p =>
                    settings.Filters.Shape.AllowedShapes.Contains(p.PathType.ToLower())).ToList();
                _logger.LogDebug("Applied shape filter ({Shapes}): {Original} → {Filtered}",
                    string.Join(", ", settings.Filters.Shape.AllowedShapes), originalCount, filtered.Count);
            }

            _logger.LogInformation("Total filtering: {Original} → {Filtered} paths", originalCount, filtered.Count);
            return filtered;
        }

        /// <summary>
        /// Bridge gaps between path endpoints within the specified tolerance.
        /// Connects paths that have endpoints close to each other or that are collinear.
        /// </summary>
        /// <param name="paths">List of paths to process</param>
        /// <param name="gapTolerance">Maximum distance to bridge gaps</param>
        /// <returns>List of paths with gaps bridged</returns>
        public List<RawPath> BridgeGaps(List<RawPath> paths, double gapTolerance)
        {
            if (paths.Count == 0 || gapTolerance <= 0)
                return paths;

            _logger.LogInformation("Bridging gaps with tolerance: {Tolerance}", gapTolerance);

            // Convert RawPath to mutable path segments for easier manipulation
            var segments = paths.Select(p => new PathSegment
            {
                Points = new List<Point2D>(p.Points),
                OriginalPath = p
            }).ToList();

            bool foundConnection;
            int iterationCount = 0;
            int maxIterations = segments.Count * 2; // Prevent infinite loops

            do
            {
                foundConnection = false;
                iterationCount++;

                if (iterationCount > maxIterations)
                {
                    _logger.LogWarning("Gap bridging exceeded max iterations ({Max})", maxIterations);
                    break;
                }

                // Try to connect segments
                for (int i = 0; i < segments.Count; i++)
                {
                    if (segments[i].Points.Count < 2)
                        continue;

                    var segment1 = segments[i];
                    var endpoint1 = segment1.Points[segment1.Points.Count - 1];

                    // Look for another segment to connect to
                    for (int j = 0; j < segments.Count; j++)
                    {
                        if (i == j || segments[j].Points.Count < 2)
                            continue;

                        var segment2 = segments[j];
                        var startpoint2 = segment2.Points[0];
                        var endpoint2 = segment2.Points[segment2.Points.Count - 1];

                        // Check if endpoint1 can connect to startpoint2
                        if (CanBridgeGap(endpoint1, startpoint2, segment1, segment2, gapTolerance, out bool removeGapPoint))
                        {
                            // Merge segment2 into segment1
                            if (removeGapPoint)
                            {
                                // Remove the last point of segment1 (it's redundant)
                                segment1.Points.RemoveAt(segment1.Points.Count - 1);
                            }
                            segment1.Points.AddRange(segment2.Points);
                            segments.RemoveAt(j);
                            foundConnection = true;
                            _logger.LogDebug("Connected segment {I} to segment {J} (forward)", i, j);
                            break;
                        }

                        // Check if endpoint1 can connect to endpoint2 (reversed)
                        if (CanBridgeGap(endpoint1, endpoint2, segment1, segment2, gapTolerance, out removeGapPoint))
                        {
                            // Merge segment2 (reversed) into segment1
                            if (removeGapPoint)
                            {
                                segment1.Points.RemoveAt(segment1.Points.Count - 1);
                            }
                            // Add segment2 points in reverse order
                            for (int k = segment2.Points.Count - 1; k >= 0; k--)
                            {
                                segment1.Points.Add(segment2.Points[k]);
                            }
                            segments.RemoveAt(j);
                            foundConnection = true;
                            _logger.LogDebug("Connected segment {I} to segment {J} (reversed)", i, j);
                            break;
                        }
                    }

                    if (foundConnection)
                        break; // Restart the search after a connection
                }

            } while (foundConnection);

            _logger.LogInformation("Gap bridging complete: {Original} → {Bridged} segments after {Iterations} iterations",
                paths.Count, segments.Count, iterationCount);

            // Convert back to RawPath objects
            var result = new List<RawPath>();
            foreach (var segment in segments)
            {
                var rawPath = new RawPath
                {
                    Points = segment.Points,
                    LineWidth = segment.OriginalPath.LineWidth,
                    IsStroked = segment.OriginalPath.IsStroked,
                    IsFilled = segment.OriginalPath.IsFilled,
                    SegmentCount = segment.Points.Count,
                    PathLength = CalculatePathLength(segment.Points),
                    PathType = DeterminePathType(segment.Points, segment.Points.Count),
                    PathTypeEnum = segment.OriginalPath.PathTypeEnum,
                    WallThickness = segment.OriginalPath.WallThickness,
                    ObjectIndex = segment.OriginalPath.ObjectIndex
                };
                result.Add(rawPath);
            }

            return result;
        }

        /// <summary>
        /// Check if two endpoints can be bridged based on distance and direction alignment
        /// Key rule: Only connect if the gap direction is collinear with at least one of the path directions
        /// This prevents connecting parallel lines that happen to be close
        /// </summary>
        private bool CanBridgeGap(Point2D endpoint1, Point2D endpoint2, PathSegment segment1, PathSegment segment2,
            double gapTolerance, out bool removeGapPoint)
        {
            removeGapPoint = false;

            // Calculate distance between endpoints
            double distance = Math.Sqrt(
                Math.Pow(endpoint2.X - endpoint1.X, 2) +
                Math.Pow(endpoint2.Y - endpoint1.Y, 2));

            // If distance is too large, cannot bridge
            if (distance > gapTolerance)
                return false;

            // If points are very close (essentially the same), we can connect and remove the duplicate
            if (distance < 0.1)
            {
                removeGapPoint = true;
                return true;
            }

            // Calculate gap direction vector
            double gapDx = endpoint2.X - endpoint1.X;
            double gapDy = endpoint2.Y - endpoint1.Y;
            double gapLength = Math.Sqrt(gapDx * gapDx + gapDy * gapDy);

            if (gapLength < 0.001)
            {
                // Points are essentially the same
                removeGapPoint = true;
                return true;
            }

            // Normalize gap direction
            gapDx /= gapLength;
            gapDy /= gapLength;

            bool gapAlignedWithPath1 = false;
            bool gapAlignedWithPath2 = false;

            // Check if gap direction is collinear with path1's direction (last segment)
            if (segment1.Points.Count >= 2)
            {
                var prevPoint1 = segment1.Points[segment1.Points.Count - 2];
                double path1Dx = endpoint1.X - prevPoint1.X;
                double path1Dy = endpoint1.Y - prevPoint1.Y;
                double path1Length = Math.Sqrt(path1Dx * path1Dx + path1Dy * path1Dy);

                if (path1Length > 0.001)
                {
                    // Normalize path1 direction
                    path1Dx /= path1Length;
                    path1Dy /= path1Length;

                    // Check if gap direction is collinear with path1 direction
                    // Use dot product: if |dot| ≈ 1, vectors are collinear (parallel or anti-parallel)
                    double dotProduct = Math.Abs(gapDx * path1Dx + gapDy * path1Dy);
                    if (dotProduct > 0.95) // ~18 degree tolerance
                    {
                        gapAlignedWithPath1 = true;
                    }
                }
            }

            // Check if gap direction is collinear with path2's direction (first segment)
            if (segment2.Points.Count >= 2)
            {
                var nextPoint2 = segment2.Points[1];
                double path2Dx = nextPoint2.X - endpoint2.X;
                double path2Dy = nextPoint2.Y - endpoint2.Y;
                double path2Length = Math.Sqrt(path2Dx * path2Dx + path2Dy * path2Dy);

                if (path2Length > 0.001)
                {
                    // Normalize path2 direction
                    path2Dx /= path2Length;
                    path2Dy /= path2Length;

                    // Check if gap direction is collinear with path2 direction
                    double dotProduct = Math.Abs(gapDx * path2Dx + gapDy * path2Dy);
                    if (dotProduct > 0.95) // ~18 degree tolerance
                    {
                        gapAlignedWithPath2 = true;
                    }
                }
            }

            // Only connect if gap is aligned with at least one path
            // This prevents connecting parallel lines (where gap is perpendicular to both paths)
            if (!gapAlignedWithPath1 && !gapAlignedWithPath2)
            {
                _logger.LogDebug("Gap not aligned with either path - skipping connection");
                return false;
            }

            return true;
        }

        /// <summary>
        /// Check if three points are approximately collinear
        /// </summary>
        private bool AreCollinear(Point2D p1, Point2D p2, Point2D p3, double tolerance)
        {
            // Calculate the cross product of vectors (p2-p1) and (p3-p2)
            // If cross product is near zero, points are collinear
            double dx1 = p2.X - p1.X;
            double dy1 = p2.Y - p1.Y;
            double dx2 = p3.X - p2.X;
            double dy2 = p3.Y - p2.Y;

            double crossProduct = Math.Abs(dx1 * dy2 - dy1 * dx2);

            // Normalize by the lengths to get a distance-like measure
            double length1 = Math.Sqrt(dx1 * dx1 + dy1 * dy1);
            double length2 = Math.Sqrt(dx2 * dx2 + dy2 * dy2);

            if (length1 < 0.001 || length2 < 0.001)
                return false; // Degenerate case

            double normalizedCross = crossProduct / (length1 * length2);

            return normalizedCross < tolerance;
        }

        /// <summary>
        /// Helper class for gap bridging - represents a mutable path segment
        /// </summary>
        private class PathSegment
        {
            public List<Point2D> Points { get; set; } = new();
            public RawPath OriginalPath { get; set; } = new();
        }

        #endregion

        #region Polygon Processing

        /// <summary>
        /// Remove outer polygons (keep only inner boundaries)
        /// This is the inverse of RemoveNestedPolygons - keeps inner polygons instead of outer ones
        /// </summary>
        private List<List<Point2D>> RemoveOuterPolygons(List<List<Point2D>> polygons)
        {
            if (polygons.Count <= 1)
                return polygons;

            var result = new List<List<Point2D>>();
            var toRemove = new HashSet<int>();

            // Check each pair of polygons
            for (int i = 0; i < polygons.Count; i++)
            {
                if (toRemove.Contains(i))
                    continue;

                for (int j = i + 1; j < polygons.Count; j++)
                {
                    if (toRemove.Contains(j))
                        continue;

                    var poly1 = polygons[i];
                    var poly2 = polygons[j];

                    // Check if one polygon is nested inside the other
                    if (IsPolygonNested(poly1, poly2, out bool poly1IsInner))
                    {
                        // Remove the OUTER polygon, keep the INNER one (inverse of RemoveNestedPolygons)
                        if (poly1IsInner)
                        {
                            toRemove.Add(j); // j is outer, remove it
                            _logger.LogDebug("Removing outer polygon {Index} - keeping inner polygon {InnerIndex}", j, i);
                        }
                        else
                        {
                            toRemove.Add(i); // i is outer, remove it
                            _logger.LogDebug("Removing outer polygon {Index} - keeping inner polygon {InnerIndex}", i, j);
                            break; // poly1 is removed, no need to check further
                        }
                    }
                }
            }

            // Add polygons that weren't removed
            for (int i = 0; i < polygons.Count; i++)
            {
                if (!toRemove.Contains(i))
                {
                    result.Add(polygons[i]);
                }
            }

            return result;
        }

        #endregion

    }
}

