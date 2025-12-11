using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using Microsoft.Extensions.Logging;
using JSBA.CloudCore.Extractor;
using JSBA.CloudCore.Extractor.Helpers;
using Xunit;
using Xunit.Abstractions;

namespace JSBA.CloudCore.Tests
{
    public class PdfVisualizationTests
    {
        private readonly ExtractionEngine _engine;
        private readonly ILogger<ExtractionEngine> _logger;
        private readonly ITestOutputHelper _output;

        public PdfVisualizationTests(ITestOutputHelper output)
        {
            _output = output;

            // Create a real logger that outputs to xUnit test output
            var loggerFactory = LoggerFactory.Create(builder =>
            {
                builder.AddProvider(new XunitLoggerProvider(output));
                builder.SetMinimumLevel(LogLevel.Information);
            });

            _logger = loggerFactory.CreateLogger<ExtractionEngine>();
            _engine = new ExtractionEngine(_logger);
        }

        [Fact]
        public void VisualizePaths_WithPdf_onlywall()
        {
            VisualizePathsForPdf("Project3_onlywall.pdf", expectedBoundaryCount: 1);
        }

        [Fact]
        public void VisualizePaths_WithPdf_doorwindow()
        {
            VisualizePathsForPdf("Project3_doorwindow.pdf", expectedBoundaryCount: 1);
        }

        [Fact]
        public void VisualizePaths_WithPdf_roomtag()
        {
            VisualizePathsForPdf("Project3_roomtag.pdf", expectedBoundaryCount: 1);
        }

        [Fact]
        public void VisualizePaths_WithPdf_MultipleRoomsWithRoomTags_TwoRooms()
        {
            VisualizePathsForPdf("Multiple rooms with room tags (no noise)_tworooms.pdf", expectedBoundaryCount: 2);
        }

        [Fact]
        public void VisualizePaths_WithPdf_MultipleRoomsWithRoomTags_TwoRooms_2()
        {
            VisualizePathsForPdf("Multiple rooms with room tags (no noise)_tworooms_2.pdf", expectedBoundaryCount: 2);
        }

        //[Fact]
        //public void VisualizePaths_WithPdf_MultipleRoomsWithRoomTags()
        //{
        //    VisualizePathsForPdf("Multiple rooms with room tags (no noise).pdf", expectedBoundaryCount: 16);
        //}

        /// <summary>
        /// Helper method to visualize paths for a given PDF file
        /// </summary>
        private void VisualizePathsForPdf(string pdfFileName, int expectedBoundaryCount)
        {
            // Arrange
            var testPdfPath = FindTestPdf(pdfFileName);
            if (!File.Exists(testPdfPath))
            {
                _output.WriteLine($"Test PDF not found: {testPdfPath}. Skipping test.");
                return;
            }

            // Derive test name from PDF file name (remove extension and sanitize)
            var testName = Path.GetFileNameWithoutExtension(pdfFileName)
                .Replace(" ", "_")
                .Replace("(", "")
                .Replace(")", "")
                .Replace(".", "");

            // settingsPath defaults to standard location if not provided
            var settings = _engine.LoadSettingsForPdf(testPdfPath);
            using var stream = File.OpenRead(testPdfPath);

            // Act - Extract boundaries (this populates AllPaths and ClosedPolygons)
            var boundaries = _engine.ExtractRoomBoundariesWithPDFiumNative(stream, settings);

            // Create unique output file names based on test name
            var safeTestName = testName;
            var mergedbridgedPathsOutput = Path.Combine(Path.GetTempPath(), $"mergedbridged_paths_{safeTestName}.png");
            var filterdPathsOutput = Path.Combine(Path.GetTempPath(), $"filtered_paths_{safeTestName}.png");
            var closedPolygonsOutput = Path.Combine(Path.GetTempPath(), $"closed_polygons_{safeTestName}.png");
            var finalBoundariesOutput = Path.Combine(Path.GetTempPath(), $"final_boundaries_{safeTestName}.png");

            TestHelper.VisualizeRawPaths(_engine.AllPaths, _engine.PageWidth, _engine.PageHeight, mergedbridgedPathsOutput);
            TestHelper.VisualizeRawPaths(_engine.FilteredPaths, _engine.PageWidth, _engine.PageHeight, filterdPathsOutput);

            TestHelper.VisualizePaths(_engine.ClosedPolygons, _engine.PageWidth, _engine.PageHeight, closedPolygonsOutput);

            // Visualize final boundaries (after removing outer polygons)
            var finalBoundaryPolygons = boundaries.Select(b => b.Polygon).ToList();
            TestHelper.VisualizePaths(finalBoundaryPolygons, _engine.PageWidth, _engine.PageHeight, finalBoundariesOutput);

            // Assert
            Assert.True(File.Exists(filterdPathsOutput), $"Filered paths visualization should be created");
            Assert.True(File.Exists(mergedbridgedPathsOutput), $"Merged-bridged paths visualization should be created");
            Assert.True(File.Exists(closedPolygonsOutput), $"Closed polygons visualization should be created");
            Assert.True(File.Exists(finalBoundariesOutput), $"Final boundaries visualization should be created");
            Assert.Equal(expectedBoundaryCount, boundaries.Count);

            _output.WriteLine($"=== PDF VISUALIZATION: {pdfFileName} ===");
            _output.WriteLine($"Test name: {testName}");
            _output.WriteLine($"Total raw paths: {_engine.AllPaths.Count}");
            _output.WriteLine($"Total closed polygons: {_engine.ClosedPolygons.Count}");
            _output.WriteLine($"Total final boundaries: {boundaries.Count} (expected: {expectedBoundaryCount})");
            _output.WriteLine($"filered paths saved to: {filterdPathsOutput}");
            _output.WriteLine($"merged-bridged paths saved to: {mergedbridgedPathsOutput}");
            _output.WriteLine($"Closed polygons saved to: {closedPolygonsOutput}");
            _output.WriteLine($"Final boundaries saved to: {finalBoundariesOutput}");
            _output.WriteLine($"Compare all three images to see the processing pipeline!");
            _output.WriteLine($"=== END VISUALIZATION ===");
        }

        private string FindTestPdf(string specificName = "")
        {
            // Look for test PDFs in the tests directory at repository root
            var baseDir = Directory.GetCurrentDirectory();
            var testsDir = Path.Combine(baseDir, "..", "..", "..", "..", "tests");
            testsDir = Path.GetFullPath(testsDir);
            specificName = string.IsNullOrEmpty(specificName) ? "*.pdf" : specificName;

            if (Directory.Exists(testsDir))
            {
                var pdfFiles = Directory.GetFiles(testsDir, specificName, SearchOption.AllDirectories);
                if (pdfFiles.Length > 0)
                {
                    return pdfFiles[0];
                }
            }

            // Fallback: try absolute path from repository root
            var repoRoot = Path.Combine(baseDir, "..", "..", "..", "..");
            repoRoot = Path.GetFullPath(repoRoot);
            var fallbackTestsDir = Path.Combine(repoRoot, "tests");
            if (Directory.Exists(fallbackTestsDir))
            {
                var pdfFiles = Directory.GetFiles(fallbackTestsDir, specificName, SearchOption.AllDirectories);
                if (pdfFiles.Length > 0)
                {
                    return pdfFiles[0];
                }
            }

            return Path.Combine(testsDir, specificName);
        }
    }
}

