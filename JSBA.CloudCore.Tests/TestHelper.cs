using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using JSBA.CloudCore.Contracts.Models;

namespace JSBA.CloudCore.Tests
{
    /// <summary>
    /// Helper class for test visualization and debugging
    /// Contains methods that should NOT be in production code
    /// </summary>
    public static class TestHelper
    {
        /// <summary>
        /// Visualize polygons as a PNG image for debugging
        /// </summary>
        /// <param name="polygons">List of polygons (each polygon is a list of points)</param>
        /// <param name="pageWidth">PDF page width</param>
        /// <param name="pageHeight">PDF page height</param>
        /// <param name="outputPath">Output PNG file path</param>
        /// <param name="lineWidth">Line width for drawing (default: 2.0)</param>
        public static void VisualizePaths(List<List<Point2D>> polygons, double pageWidth, double pageHeight, string outputPath, float lineWidth = 2.0f)
        {
            if (polygons == null || polygons.Count == 0)
            {
                Console.WriteLine("No polygons to visualize");
                return;
            }

            try
            {
                // Find bounding box of all points
                double minX = double.MaxValue;
                double minY = double.MaxValue;
                double maxX = double.MinValue;
                double maxY = double.MinValue;

                foreach (var polygon in polygons)
                {
                    foreach (var pt in polygon)
                    {
                        minX = Math.Min(minX, pt.X);
                        minY = Math.Min(minY, pt.Y);
                        maxX = Math.Max(maxX, pt.X);
                        maxY = Math.Max(maxY, pt.Y);
                    }
                }

                double contentWidth = maxX - minX;
                double contentHeight = maxY - minY;

                Console.WriteLine($"Bounding box: X=[{minX:F2}, {maxX:F2}], Y=[{minY:F2}, {maxY:F2}]");
                Console.WriteLine($"Content size: {contentWidth:F2} x {contentHeight:F2}");

                // Add padding (10% on each side)
                double padding = 0.1;
                int imageWidth = 1200;
                int imageHeight = (int)(imageWidth * (contentHeight / contentWidth));

                using (var bitmap = new Bitmap(imageWidth, imageHeight))
                using (var graphics = Graphics.FromImage(bitmap))
                {
                    // White background
                    graphics.Clear(Color.White);
                    graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;

                    // Calculate scale factors with padding
                    double scaleX = imageWidth * (1 - 2 * padding) / contentWidth;
                    double scaleY = imageHeight * (1 - 2 * padding) / contentHeight;
                    double scale = Math.Min(scaleX, scaleY); // Use uniform scale

                    double offsetX = (imageWidth - contentWidth * scale) / 2;
                    double offsetY = (imageHeight - contentHeight * scale) / 2;

                    // Draw each polygon
                    var random = new Random(42); // Fixed seed for consistent colors
                    foreach (var polygon in polygons)
                    {
                        if (polygon.Count < 2)
                        {
                            // Draw single point as a dot
                            if (polygon.Count == 1)
                            {
                                var pt = polygon[0];
                                int x = (int)((pt.X - minX) * scale + offsetX);
                                int y = (int)((maxY - pt.Y) * scale + offsetY); // Flip Y
                                graphics.FillEllipse(Brushes.Red, x - 3, y - 3, 6, 6);
                            }
                            continue;
                        }

                        // Generate a random color for this polygon
                        var color = Color.FromArgb(random.Next(256), random.Next(256), random.Next(256));
                        var pen = new Pen(color, lineWidth);

                        // Draw lines between points
                        for (int i = 0; i < polygon.Count - 1; i++)
                        {
                            var p1 = polygon[i];
                            var p2 = polygon[i + 1];

                            int x1 = (int)((p1.X - minX) * scale + offsetX);
                            int y1 = (int)((maxY - p1.Y) * scale + offsetY); // Flip Y
                            int x2 = (int)((p2.X - minX) * scale + offsetX);
                            int y2 = (int)((maxY - p2.Y) * scale + offsetY); // Flip Y

                            graphics.DrawLine(pen, x1, y1, x2, y2);
                        }

                        // Draw points as small circles
                        foreach (var pt in polygon)
                        {
                            int x = (int)((pt.X - minX) * scale + offsetX);
                            int y = (int)((maxY - pt.Y) * scale + offsetY); // Flip Y
                            graphics.FillEllipse(Brushes.Black, x - 2, y - 2, 4, 4);
                        }

                        pen.Dispose();
                    }

                    // Save image
                    bitmap.Save(outputPath, ImageFormat.Png);
                    Console.WriteLine($"Saved polygon visualization to: {outputPath}");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error visualizing polygons: {ex.Message}");
            }
        }

        /// <summary>
        /// Visualize raw paths (converts RawPath to List of polygons)
        /// </summary>
        public static void VisualizeRawPaths(List<RawPath> allPaths, double pageWidth, double pageHeight, string outputPath)
        {
            if (allPaths == null || allPaths.Count == 0)
            {
                Console.WriteLine("No raw paths to visualize");
                return;
            }

            // Convert RawPath to List<List<Point2D>>
            var polygons = allPaths.Select(p => p.Points).ToList();
            VisualizePaths(polygons, pageWidth, pageHeight, outputPath, 1.0f);
        }

        /// <summary>
        /// Print path statistics to console
        /// </summary>
        public static void PrintPathStatistics(List<RawPath> allPaths, double pageWidth, double pageHeight)
        {
            Console.WriteLine($"=== PATH STATISTICS ===");
            Console.WriteLine($"Page dimensions: {pageWidth:F2} x {pageHeight:F2}");
            Console.WriteLine($"Total paths: {allPaths.Count}");
            Console.WriteLine();

            // Group by path type
            var byType = allPaths.GroupBy(p => p.PathType);
            foreach (var group in byType)
            {
                Console.WriteLine($"{group.Key}: {group.Count()} paths");
            }
            Console.WriteLine();

            // Statistics
            if (allPaths.Count > 0)
            {
                Console.WriteLine($"Line width range: {allPaths.Min(p => p.LineWidth):F2} - {allPaths.Max(p => p.LineWidth):F2}");
                Console.WriteLine($"Path length range: {allPaths.Min(p => p.PathLength):F2} - {allPaths.Max(p => p.PathLength):F2}");
                Console.WriteLine($"Average path length: {allPaths.Average(p => p.PathLength):F2}");
            }

            Console.WriteLine($"=== END STATISTICS ===");
        }

        /// <summary>
        /// Print detailed information about first N paths
        /// </summary>
        public static void PrintPathDetails(List<RawPath> allPaths, int count = 10)
        {
            Console.WriteLine($"=== PATH DETAILS (first {count}) ===");

            foreach (var path in allPaths.Take(count))
            {
                Console.WriteLine($"Path #{path.ObjectIndex}:");
                Console.WriteLine($"  Type: {path.PathType}");
                Console.WriteLine($"  Points: {path.Points.Count}");
                Console.WriteLine($"  Length: {path.PathLength:F2}");
                Console.WriteLine($"  Line Width: {path.LineWidth:F2}");
                Console.WriteLine($"  Stroked: {path.IsStroked}, Filled: {path.IsFilled}");
                Console.WriteLine($"  Segments: {path.SegmentCount}");
            }

            Console.WriteLine($"=== END DETAILS ===");
        }
    }
}

