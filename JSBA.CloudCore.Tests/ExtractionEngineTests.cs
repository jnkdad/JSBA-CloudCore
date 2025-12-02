using System;
using System.IO;
using System.Linq;
using Microsoft.Extensions.Logging;
using Moq;
using JSBA.CloudCore.Contracts.Models;
using JSBA.CloudCore.Extractor;
using Xunit;
using Xunit.Abstractions;

namespace JSBA.CloudCore.Tests
{
    public class ExtractionEngineTests
    {
        private readonly ExtractionEngine _engine;
        private readonly ILogger<ExtractionEngine> _logger;
        private readonly ITestOutputHelper _output;

        public ExtractionEngineTests(ITestOutputHelper output)
        {
            _output = output;

            // Create a real logger that outputs to xUnit test output
            var loggerFactory = LoggerFactory.Create(builder =>
            {
                builder.AddProvider(new XunitLoggerProvider(output));
                builder.SetMinimumLevel(LogLevel.Information); // Show Information and above
            });

            _logger = loggerFactory.CreateLogger<ExtractionEngine>();
            _engine = new ExtractionEngine(_logger);
        }

        [Fact]
        public void ProcessPdfToRooms_WithValidPdf_ReturnsRoomsResult()
        {
            // Arrange
            var testPdfPath = FindTestPdf("Project3_onlywall.pdf");
            Assert.True(File.Exists(testPdfPath), $"Test PDF not found at {testPdfPath}");

            using var stream = File.OpenRead(testPdfPath);
            var options = new PdfOptions();

            // Act
            var result = _engine.ProcessPdfToRooms(stream, options);

            // Assert
            Assert.NotNull(result);
            Assert.NotNull(result.Rooms);
            Assert.NotNull(result.Metadata);
            Assert.True(result.Metadata.PageCount > 0, "PDF should have at least one page");
        }

        [Fact]
        public void ProcessPdfToRooms_WithRoomTagPdf_ExtractsRooms()
        {
            // Arrange
            var testPdfPath = FindTestPdf("Project3_roomtag.pdf");
            Assert.True(File.Exists(testPdfPath), $"Test PDF not found at {testPdfPath}");
            using var stream = File.OpenRead(testPdfPath);
            var options = new PdfOptions();

            // Act
            var result = _engine.ProcessPdfToRooms(stream, options);

            // Assert
            Assert.NotEmpty(result.Rooms);
            
            // Each room should have required properties
            foreach (var room in result.Rooms)
            {
                Assert.NotNull(room.Id);
                Assert.NotEmpty(room.Id);
                Assert.NotNull(room.Polygon);
                Assert.True(room.Polygon.Count >= 3, "Room polygon should have at least 3 points");
            }
        }

        [Fact]
        public void ProcessPdfToRooms_RoomPolygons_HaveValidCoordinates()
        {
            // Arrange
            var testPdfPath = FindTestPdf("Project3_onlywall.pdf");
            using var stream = File.OpenRead(testPdfPath);
            var options = new PdfOptions();

            // Act
            var result = _engine.ProcessPdfToRooms(stream, options);

            // Assert
            foreach (var room in result.Rooms)
            {
                foreach (var point in room.Polygon)
                {
                    Assert.True(double.IsFinite(point.X), $"Room {room.Id} has invalid X coordinate");
                    Assert.True(double.IsFinite(point.Y), $"Room {room.Id} has invalid Y coordinate");
                }
            }
        }

        [Fact]
        public void ProcessPdfToRooms_Metadata_HasCorrectUnits()
        {
            // Arrange
            var testPdfPath = FindTestPdf("Project3_onlywall.pdf");
            using var stream = File.OpenRead(testPdfPath);
            var options = new PdfOptions();

            // Act
            var result = _engine.ProcessPdfToRooms(stream, options);

            // Assert
            Assert.NotNull(result.Metadata.Units);
            Assert.Equal("feet", result.Metadata.Units);
        }

        [Fact]
        public void ProcessPdfToRooms_WithInvalidStream_ThrowsException()
        {
            // Arrange
            using var emptyStream = new MemoryStream();
            var options = new PdfOptions();

            // Act & Assert
            Assert.ThrowsAny<Exception>(() => _engine.ProcessPdfToRooms(emptyStream, options));
        }

        [Fact]
        public void ComparePdfPigVsPDFiumNative_Project3OnlyWall()
        {
            // Arrange
            var testPdfPath = FindTestPdf("Project3_onlywall.pdf");
            Assert.True(File.Exists(testPdfPath), $"Test PDF not found at {testPdfPath}");

            // Act - Compare both extraction methods
            using var stream = File.OpenRead(testPdfPath);
            var comparison = _engine.CompareBoundaryExtractionMethods(stream);

            // Assert - Log comparison results
            Console.WriteLine("=== COMPARISON: PdfPig vs PDFium Native ===");
            Console.WriteLine($"File: {Path.GetFileName(testPdfPath)}");
            Console.WriteLine();
            Console.WriteLine($"PdfPig:        {comparison.PdfPigBoundaries.Count} boundaries extracted");
            Console.WriteLine($"PDFium Native: {comparison.PDFiumNativeBoundaries.Count} boundaries extracted");
            Console.WriteLine();

            // Compare each boundary
            Console.WriteLine("--- PdfPig Boundaries ---");
            for (int i = 0; i < comparison.PdfPigBoundaries.Count; i++)
            {
                var boundary = comparison.PdfPigBoundaries[i];
                Console.WriteLine($"Boundary {i}: {boundary.Count} points");
                for (int j = 0; j < Math.Min(boundary.Count, 10); j++)
                {
                    var pt = boundary[j];
                    Console.WriteLine($"  Point[{j}]: X={pt.X:F2}, Y={pt.Y:F2}");
                }
                if (boundary.Count > 10)
                {
                    Console.WriteLine($"  ... ({boundary.Count - 10} more points)");
                }
            }

            Console.WriteLine();
            Console.WriteLine("--- PDFium Native Boundaries ---");
            for (int i = 0; i < comparison.PDFiumNativeBoundaries.Count; i++)
            {
                var boundary = comparison.PDFiumNativeBoundaries[i];
                Console.WriteLine($"Boundary {i}: {boundary.Count} points");
                for (int j = 0; j < Math.Min(boundary.Count, 10); j++)
                {
                    var pt = boundary[j];
                    Console.WriteLine($"  Point[{j}]: X={pt.X:F2}, Y={pt.Y:F2}");
                }
                if (boundary.Count > 10)
                {
                    Console.WriteLine($"  ... ({boundary.Count - 10} more points)");
                }
            }

            Console.WriteLine();
            Console.WriteLine("=== END COMPARISON ===");

            // Basic assertions
            Assert.True(comparison.PdfPigBoundaries.Count > 0 || comparison.PDFiumNativeBoundaries.Count > 0,
                "At least one method should extract boundaries");
        }

        [Fact]
        public void ComparePdfPigVsPDFiumNative_Project3RoomTag()
        {
            // Arrange
            var testPdfPath = FindTestPdf("Project3_roomtag.pdf");
            if (!File.Exists(testPdfPath))
            {
                Console.WriteLine($"Skipping test: {testPdfPath} not found");
                return;
            }

            // Act - Compare both extraction methods
            using var stream = File.OpenRead(testPdfPath);
            var comparison = _engine.CompareBoundaryExtractionMethods(stream);

            // Assert - Log comparison results
            Console.WriteLine("=== COMPARISON: PdfPig vs PDFium Native ===");
            Console.WriteLine($"File: {Path.GetFileName(testPdfPath)}");
            Console.WriteLine();
            Console.WriteLine($"PdfPig:        {comparison.PdfPigBoundaries.Count} boundaries extracted");
            Console.WriteLine($"PDFium Native: {comparison.PDFiumNativeBoundaries.Count} boundaries extracted");
            Console.WriteLine();

            // Basic assertions
            Assert.True(comparison.PdfPigBoundaries.Count > 0 || comparison.PDFiumNativeBoundaries.Count > 0,
                "At least one method should extract boundaries");
        }

        private string FindTestPdf(string specificName = "")
        {
            // Look for test PDFs in the samples/vector directory
            var baseDir = Directory.GetCurrentDirectory();
            var samplesDir = Path.Combine(baseDir, "..", "..", "..", "..", "..", "samples", "vector");
            specificName = string.IsNullOrEmpty(specificName) ? "*.pdf" : specificName;

            if (Directory.Exists(samplesDir))
            {
                var pdfFiles = Directory.GetFiles(samplesDir, specificName);
                if (pdfFiles.Length > 0)
                {
                    return pdfFiles[0];
                }
            }

            // Fallback: search from repository root
            var repoRootSamples = Path.Combine(baseDir, "samples", "vector");
            if (Directory.Exists(repoRootSamples))
            {
                var pdfFiles = Directory.GetFiles(repoRootSamples, specificName);
                if (pdfFiles.Length > 0)
                {
                    return pdfFiles[0];
                }
            }

            throw new FileNotFoundException($"No test PDF files found. Please ensure samples/vector directory contains PDF files. Searched: {samplesDir}");
        }

        [Fact]
        public void CompareTextExtraction_PdfPigVsPDFiumNative_Project3RoomTag()
        {
            // Arrange
            var testPdfPath = FindTestPdf("Project3_roomtag.pdf");
            Assert.True(File.Exists(testPdfPath), $"Test PDF not found at {testPdfPath}");

            // Act - Compare both text extraction methods
            using var stream = File.OpenRead(testPdfPath);
            var comparison = _engine.CompareTextExtractionMethods(stream);

            // Assert - Log comparison results
            Console.WriteLine("=== TEXT EXTRACTION COMPARISON: PdfPig vs PDFium Native ===");
            Console.WriteLine($"File: {Path.GetFileName(testPdfPath)}");
            Console.WriteLine($"PdfPig:        {comparison.PdfPigLabels.Count} text labels extracted");
            Console.WriteLine($"PDFium Native: {comparison.PDFiumNativeLabels.Count} text labels extracted");
            Console.WriteLine();

            // Show PdfPig results
            Console.WriteLine("--- PdfPig Text Labels ---");
            foreach (var label in comparison.PdfPigLabels.Take(20))
            {
                Console.WriteLine($"  '{label.Text}' at ({label.CenterX:F2}, {label.CenterY:F2})");
            }
            if (comparison.PdfPigLabels.Count > 20)
            {
                Console.WriteLine($"  ... and {comparison.PdfPigLabels.Count - 20} more");
            }
            Console.WriteLine();

            // Show PDFium Native results
            Console.WriteLine("--- PDFium Native Text Labels ---");
            foreach (var label in comparison.PDFiumNativeLabels.Take(20))
            {
                Console.WriteLine($"  '{label.Text}' at ({label.CenterX:F2}, {label.CenterY:F2})");
            }
            if (comparison.PDFiumNativeLabels.Count > 20)
            {
                Console.WriteLine($"  ... and {comparison.PDFiumNativeLabels.Count - 20} more");
            }
            Console.WriteLine();

            // Compare specific labels
            Console.WriteLine("--- Comparison Analysis ---");
            var pdfPigTexts = comparison.PdfPigLabels.Select(l => l.Text).ToHashSet();
            var pdfiumTexts = comparison.PDFiumNativeLabels.Select(l => l.Text).ToHashSet();

            var onlyInPdfPig = pdfPigTexts.Except(pdfiumTexts).ToList();
            var onlyInPDFium = pdfiumTexts.Except(pdfPigTexts).ToList();
            var inBoth = pdfPigTexts.Intersect(pdfiumTexts).ToList();

            Console.WriteLine($"Common labels: {inBoth.Count}");
            Console.WriteLine($"Only in PdfPig: {onlyInPdfPig.Count}");
            if (onlyInPdfPig.Count > 0 && onlyInPdfPig.Count <= 10)
            {
                Console.WriteLine($"  {string.Join(", ", onlyInPdfPig)}");
            }
            Console.WriteLine($"Only in PDFium Native: {onlyInPDFium.Count}");
            if (onlyInPDFium.Count > 0 && onlyInPDFium.Count <= 10)
            {
                Console.WriteLine($"  {string.Join(", ", onlyInPDFium)}");
            }

            Console.WriteLine("=== END TEXT COMPARISON ===");

            Assert.True(comparison.PdfPigLabels.Count > 0 || comparison.PDFiumNativeLabels.Count > 0,
                "At least one method should extract text labels");
        }

        [Fact]
        public void CompareTextExtraction_PdfPigVsPDFiumNative_Project3OnlyWall()
        {
            // Arrange
            var testPdfPath = FindTestPdf("Project3_onlywall.pdf");
            Assert.True(File.Exists(testPdfPath), $"Test PDF not found at {testPdfPath}");

            // Act - Compare both text extraction methods
            using var stream = File.OpenRead(testPdfPath);
            var comparison = _engine.CompareTextExtractionMethods(stream);

            // Assert - Log comparison results
            Console.WriteLine("=== TEXT EXTRACTION COMPARISON: PdfPig vs PDFium Native ===");
            Console.WriteLine($"File: {Path.GetFileName(testPdfPath)}");
            Console.WriteLine($"PdfPig:        {comparison.PdfPigLabels.Count} text labels extracted");
            Console.WriteLine($"PDFium Native: {comparison.PDFiumNativeLabels.Count} text labels extracted");
            Console.WriteLine();

            Console.WriteLine("--- PdfPig Text Labels ---");
            foreach (var label in comparison.PdfPigLabels)
            {
                Console.WriteLine($"  '{label.Text}' at ({label.CenterX:F2}, {label.CenterY:F2})");
            }
            Console.WriteLine();

            Console.WriteLine("--- PDFium Native Text Labels ---");
            foreach (var label in comparison.PDFiumNativeLabels)
            {
                Console.WriteLine($"  '{label.Text}' at ({label.CenterX:F2}, {label.CenterY:F2})");
            }

            Console.WriteLine("=== END TEXT COMPARISON ===");

            // This PDF has no text, so both should return 0
            Assert.Equal(0, comparison.PdfPigLabels.Count);
            Assert.Equal(0, comparison.PDFiumNativeLabels.Count);
        }

        [Fact]
        public void ExtractRoomBoundaries_PopulatesAllPaths()
        {
            // Arrange
            var testPdfPath = FindTestPdf("Project3_onlywall.pdf");
            Assert.True(File.Exists(testPdfPath), $"Test PDF not found at {testPdfPath}");

            using var stream = File.OpenRead(testPdfPath);

            // Act - Extract boundaries (this populates AllPaths)
            var boundaries = _engine.ExtractRoomBoundariesWithPDFiumNative(stream);

            // Assert - AllPaths should be populated
            Assert.NotNull(_engine.AllPaths);
            Assert.True(_engine.PageWidth > 0, "Page width should be positive");
            Assert.True(_engine.PageHeight > 0, "Page height should be positive");
            Assert.True(_engine.AllPaths.Count > 0, "Should extract at least one path");

            // Print path details using TestHelper
            TestHelper.PrintPathDetails(_engine.AllPaths, 10);
            TestHelper.PrintPathStatistics(_engine.AllPaths, _engine.PageWidth, _engine.PageHeight);
        }

        [Fact]
        public void VisualizePaths_AfterExtraction_CreatesImage()
        {
            // Arrange
            var testPdfPath = FindTestPdf("Project3_onlywall.pdf");
            Assert.True(File.Exists(testPdfPath), $"Test PDF not found at {testPdfPath}");

            using var stream = File.OpenRead(testPdfPath);

            // Act - Extract boundaries (this populates AllPaths and ClosedPolygons)
            var boundaries = _engine.ExtractRoomBoundariesWithPDFiumNative(stream);

            // Visualize raw paths
            var rawPathsOutput = Path.Combine(Path.GetTempPath(), "raw_paths_visualization.png");
            TestHelper.VisualizeRawPaths(_engine.AllPaths, _engine.PageWidth, _engine.PageHeight, rawPathsOutput);

            // Visualize closed polygons
            var closedPolygonsOutput = Path.Combine(Path.GetTempPath(), "closed_polygons_visualization.png");
            TestHelper.VisualizePaths(_engine.ClosedPolygons, _engine.PageWidth, _engine.PageHeight, closedPolygonsOutput);

            // Assert
            Assert.True(File.Exists(rawPathsOutput), $"Raw paths visualization should be created at {rawPathsOutput}");
            Assert.True(File.Exists(closedPolygonsOutput), $"Closed polygons visualization should be created at {closedPolygonsOutput}");

            Console.WriteLine($"Raw paths visualization saved to: {rawPathsOutput}");
            Console.WriteLine($"Closed polygons visualization saved to: {closedPolygonsOutput}");
            Console.WriteLine($"Total raw paths: {_engine.AllPaths.Count}");
            Console.WriteLine($"Total closed polygons: {_engine.ClosedPolygons.Count}");

            // Cleanup
            // File.Delete(rawPathsOutput); // Keep for manual inspection
            // File.Delete(closedPolygonsOutput); // Keep for manual inspection
        }

        [Fact]
        public void LoadSettings_WithValidJson_ReturnsSettings()
        {
            // Arrange
            var settingsPath = Path.Combine(
                Directory.GetCurrentDirectory(),
                "..", "..", "..", "..", "CloudCore.VectorExtraction", "extraction-settings.json");

            // Act
            var settings = _engine.LoadSettings(settingsPath);

            // Assert
            Assert.NotNull(settings);
            Assert.NotNull(settings.Filters);
            Assert.NotNull(settings.Polygon);

            Console.WriteLine($"=== LOADED SETTINGS ===");
            Console.WriteLine($"Line Width Filter: Enabled={settings.Filters.LineWidth.Enabled}, Min={settings.Filters.LineWidth.MinWidth}, Max={settings.Filters.LineWidth.MaxWidth}");
            Console.WriteLine($"Length Filter: Enabled={settings.Filters.Length.Enabled}, Min={settings.Filters.Length.MinLength}");
            Console.WriteLine($"Shape Filter: Enabled={settings.Filters.Shape.Enabled}, Shapes={string.Join(", ", settings.Filters.Shape.AllowedShapes)}");
            Console.WriteLine($"Polygon: RemoveNested={settings.Polygon.RemoveNested}, RemoveOuter={settings.Polygon.RemoveOuter}, GapTolerance={settings.Polygon.GapTolerance}");
            Console.WriteLine($"=== END SETTINGS ===");
        }

        [Fact]
        public void FilterPaths_WithLengthFilter_FiltersShortPaths()
        {
            // Arrange
            var testPdfPath = FindTestPdf("Project3_doorwindow.pdf");
            Assert.True(File.Exists(testPdfPath), $"Test PDF not found at {testPdfPath}");

            using var stream = File.OpenRead(testPdfPath);

            // Extract boundaries (this populates AllPaths)
            var boundaries = _engine.ExtractRoomBoundariesWithPDFiumNative(stream);

            var settings = new ExtractionSettings
            {
                Filters = new PathFilters
                {
                    Length = new LengthFilter
                    {
                        Enabled = true,
                        MinLength = 50.0 // Filter out paths shorter than 50 units
                    }
                }
            };

            // Act - Filter the paths that were actually extracted
            var filteredPaths = _engine.FilterPaths(_engine.AllPaths, settings, _engine.PageWidth, _engine.PageHeight);

            var filteredPathsOutput = Path.Combine(Path.GetTempPath(), "filtered_Paths.png");
            TestHelper.VisualizeRawPaths(filteredPaths, _engine.PageWidth, _engine.PageHeight, filteredPathsOutput);
            // Assert
            Assert.True(filteredPaths.Count <= _engine.AllPaths.Count, "Filtered count should be <= original count");
            Assert.All(filteredPaths, path => Assert.True(path.PathLength >= 50.0, "All filtered paths should meet minimum length"));

            Console.WriteLine($"=== PATH FILTERING ===");
            Console.WriteLine($"Original paths: {_engine.AllPaths.Count}");
            Console.WriteLine($"Filtered paths: {filteredPaths.Count}");
            Console.WriteLine($"Removed: {_engine.AllPaths.Count - filteredPaths.Count}");
            Console.WriteLine($"=== END FILTERING ===");
        }

        [Fact]
        public void VisualizePaths_WithComplexPdf_ShowsAllPaths()
        {
            // Arrange - Use a more complex PDF
            //var testPdfPath = FindTestPdf("Project3_onlywall.pdf");
            //var testPdfPath = FindTestPdf("Project3_doorwindow.pdf");
            var testPdfPath = FindTestPdf("EA-151c - REFLECTED CEILING PLAN - FIRST FLOOR LEVEL AREA c.pdf");
            if (!File.Exists(testPdfPath))
            {
                Console.WriteLine($"Test PDF not found: {testPdfPath}. Skipping test.");
                return;
            }
            var settingsPath = Path.Combine(AppContext.BaseDirectory, "extraction-settings.json");
            var settings = _engine.LoadSettingsForPdf(settingsPath, testPdfPath);
            using var stream = File.OpenRead(testPdfPath);

            // Act - Extract boundaries (this populates AllPaths and ClosedPolygons)
            var boundaries = _engine.ExtractRoomBoundariesWithPDFiumNative(stream, settings);

            // Visualize raw paths, closed polygons, and final boundaries
            var rawPathsOutput = Path.Combine(Path.GetTempPath(), "raw_paths.png");
            var closedPolygonsOutput = Path.Combine(Path.GetTempPath(), "closed_polygons.png");
            var finalBoundariesOutput = Path.Combine(Path.GetTempPath(), "final_boundaries.png");

            TestHelper.VisualizeRawPaths(_engine.AllPaths, _engine.PageWidth, _engine.PageHeight, rawPathsOutput);
            TestHelper.VisualizePaths(_engine.ClosedPolygons, _engine.PageWidth, _engine.PageHeight, closedPolygonsOutput);

            // Visualize final boundaries (after removing outer polygons)
            var finalBoundaryPolygons = boundaries.Select(b => b.Polygon).ToList();
            TestHelper.VisualizePaths(finalBoundaryPolygons, _engine.PageWidth, _engine.PageHeight, finalBoundariesOutput);

            // Assert
            Assert.True(File.Exists(rawPathsOutput), $"Raw paths visualization should be created");
            Assert.True(File.Exists(closedPolygonsOutput), $"Closed polygons visualization should be created");
            Assert.True(File.Exists(finalBoundariesOutput), $"Final boundaries visualization should be created");

            Console.WriteLine($"=== COMPLEX PDF VISUALIZATION ===");
            Console.WriteLine($"Total raw paths: {_engine.AllPaths.Count}");
            Console.WriteLine($"Total closed polygons: {_engine.ClosedPolygons.Count}");
            Console.WriteLine($"Total final boundaries: {boundaries.Count}");
            Console.WriteLine($"Raw paths saved to: {rawPathsOutput}");
            Console.WriteLine($"Closed polygons saved to: {closedPolygonsOutput}");
            Console.WriteLine($"Final boundaries saved to: {finalBoundariesOutput}");
            Console.WriteLine($"Compare all three images to see the processing pipeline!");
            Console.WriteLine($"=== END VISUALIZATION ===");
        }

        [Fact]
        public void ExtractWithSettings_AppliesFilteringAndGapBridging()
        {
            // Arrange - Use a simpler PDF for faster testing
            var testPdfPath = FindTestPdf("Project3_onlywall.pdf");
            Assert.True(File.Exists(testPdfPath), $"Test PDF not found at {testPdfPath}");

            var settingsPath = Path.Combine(
                Directory.GetCurrentDirectory(),
                "..", "..", "..", "..", "CloudCore.VectorExtraction", "extraction-settings.json");

            var settings = _engine.LoadSettings(settingsPath);

            using var stream = File.OpenRead(testPdfPath);

            // Act - Extract with settings (applies filtering and gap bridging)
            var boundaries = _engine.ExtractRoomBoundariesWithPDFiumNative(stream, settings);

            // Visualize the results
            var beforeFilterOutput = Path.Combine(Path.GetTempPath(), "before_filter_and_bridge.png");
            var afterFilterOutput = Path.Combine(Path.GetTempPath(), "after_filter_and_bridge.png");
            var closedPolygonsOutput = Path.Combine(Path.GetTempPath(), "closed_polygons_with_settings.png");

            // Note: AllPaths contains the original unfiltered paths
            // We need to extract again without settings to compare
            stream.Position = 0;
            var boundariesWithoutSettings = _engine.ExtractRoomBoundariesWithPDFiumNative(stream);
            var allPathsWithoutSettings = new List<RawPath>(_engine.AllPaths);

            // Extract again with settings
            stream.Position = 0;
            boundaries = _engine.ExtractRoomBoundariesWithPDFiumNative(stream, settings);

            TestHelper.VisualizeRawPaths(allPathsWithoutSettings, _engine.PageWidth, _engine.PageHeight, beforeFilterOutput);
            TestHelper.VisualizePaths(_engine.ClosedPolygons, _engine.PageWidth, _engine.PageHeight, closedPolygonsOutput);

            var finalBoundaryPolygons = boundaries.Select(b => b.Polygon).ToList();
            TestHelper.VisualizePaths(finalBoundaryPolygons, _engine.PageWidth, _engine.PageHeight, afterFilterOutput);

            // Assert
            Console.WriteLine($"=== EXTRACTION WITH SETTINGS ===");
            Console.WriteLine($"Settings loaded from: {settingsPath}");
            Console.WriteLine($"Length filter enabled: {settings.Filters.Length.Enabled}, MinLength: {settings.Filters.Length.MinLength}");
            Console.WriteLine($"Gap tolerance: {settings.Polygon.GapTolerance}");
            Console.WriteLine($"Original paths (before filter): {allPathsWithoutSettings.Count}");
            Console.WriteLine($"Closed polygons: {_engine.ClosedPolygons.Count}");
            Console.WriteLine($"Final boundaries: {boundaries.Count}");
            Console.WriteLine($"Before filter visualization: {beforeFilterOutput}");
            Console.WriteLine($"After filter & bridge visualization: {afterFilterOutput}");
            Console.WriteLine($"Closed polygons visualization: {closedPolygonsOutput}");
            Console.WriteLine($"=== END EXTRACTION WITH SETTINGS ===");
        }

        [Fact]
        public void BridgeGaps_ConnectsNearbyPaths()
        {
            // Arrange - Create test paths with small gaps
            var path1 = new RawPath
            {
                Points = new List<Point2D>
                {
                    new Point2D { X = 0, Y = 0 },
                    new Point2D { X = 100, Y = 0 }
                },
                PathLength = 100,
                LineWidth = 1.0,
                IsStroked = true,
                PathType = "Line"
            };

            var path2 = new RawPath
            {
                Points = new List<Point2D>
                {
                    new Point2D { X = 110, Y = 0 }, // 10 units gap from path1
                    new Point2D { X = 200, Y = 0 }
                },
                PathLength = 90,
                LineWidth = 1.0,
                IsStroked = true,
                PathType = "Line"
            };

            var path3 = new RawPath
            {
                Points = new List<Point2D>
                {
                    new Point2D { X = 300, Y = 0 }, // 100 units gap - should NOT connect
                    new Point2D { X = 400, Y = 0 }
                },
                PathLength = 100,
                LineWidth = 1.0,
                IsStroked = true,
                PathType = "Line"
            };

            var paths = new List<RawPath> { path1, path2, path3 };

            // Act - Bridge gaps with tolerance of 50 units
            var bridgedPaths = _engine.BridgeGaps(paths, gapTolerance: 50.0);

            // Assert
            Assert.True(bridgedPaths.Count < paths.Count, "Gap bridging should reduce the number of paths");
            Assert.Contains(bridgedPaths, p => p.Points.Count > 2); // At least one path should be merged

            Console.WriteLine($"=== GAP BRIDGING TEST ===");
            Console.WriteLine($"Original paths: {paths.Count}");
            Console.WriteLine($"Bridged paths: {bridgedPaths.Count}");
            Console.WriteLine($"Gap tolerance: 50.0");

            for (int i = 0; i < bridgedPaths.Count; i++)
            {
                Console.WriteLine($"Bridged path {i}: {bridgedPaths[i].Points.Count} points, length: {bridgedPaths[i].PathLength:F2}");
            }
            Console.WriteLine($"=== END GAP BRIDGING TEST ===");
        }
    }

    /// <summary>
    /// Logger provider that outputs to xUnit test output
    /// </summary>
    public class XunitLoggerProvider : ILoggerProvider
    {
        private readonly ITestOutputHelper _output;

        public XunitLoggerProvider(ITestOutputHelper output)
        {
            _output = output;
        }

        public ILogger CreateLogger(string categoryName)
        {
            return new XunitLogger(_output, categoryName);
        }

        public void Dispose() { }
    }

    /// <summary>
    /// Logger that writes to xUnit test output
    /// </summary>
    public class XunitLogger : ILogger
    {
        private readonly ITestOutputHelper _output;
        private readonly string _categoryName;

        public XunitLogger(ITestOutputHelper output, string categoryName)
        {
            _output = output;
            _categoryName = categoryName;
        }

        public IDisposable BeginScope<TState>(TState state) => null!;

        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Information;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel))
                return;

            try
            {
                var message = formatter(state, exception);
                var logMessage = $"[{logLevel}] {_categoryName}: {message}";

                // Write to xUnit test output
                _output.WriteLine(logMessage);

                // Also write to Console so it appears in VSCode Debug Console
                Console.WriteLine(logMessage);

                if (exception != null)
                {
                    var exceptionMessage = $"Exception: {exception}";
                    _output.WriteLine(exceptionMessage);
                    Console.WriteLine(exceptionMessage);
                }
            }
            catch
            {
                // Ignore errors writing to test output
            }
        }
    }
}

