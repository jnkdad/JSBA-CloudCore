using JSBA.CloudCore.Contracts.Models;
using Microsoft.Extensions.Logging;
using NetTopologySuite.Geometries;
using NetTopologySuite.Operation.Polygonize;
using NetTopologySuite.Operation.Buffer;

namespace JSBA.CloudCore.Extractor.Helpers;

/// <summary>
/// Uses NetTopologySuite for robust polygon reconstruction from line segments
/// </summary>
public class NtsPolygonizer
{
    private readonly ILogger<NtsPolygonizer> _logger;
    private readonly GeometryFactory _geometryFactory;

    public NtsPolygonizer(ILogger<NtsPolygonizer> logger)
    {
        _logger = logger;
        _geometryFactory = new GeometryFactory();
    }

    /// <summary>
    /// Convert RawPath to NTS LineString
    /// </summary>
    private LineString ToLineString(RawPath path)
    {
        var coordinates = path.Points.Select(p => new Coordinate(p.X, p.Y)).ToArray();
        return _geometryFactory.CreateLineString(coordinates);
    }

    /// <summary>
    /// Convert NTS Polygon to RoomBoundary
    /// </summary>
    private RoomBoundary ToRoomBoundary(Polygon polygon)
    {
        var points = polygon.ExteriorRing.Coordinates
            .Select(c => new Point2D { X = c.X, Y = c.Y })
            .ToList();

        return new RoomBoundary
        {
            Polygon = points
        };
    }

    /// <summary>
    /// Get area of a RoomBoundary
    /// </summary>
    private double GetArea(RoomBoundary boundary)
    {
        var coords = boundary.Polygon.Select(p => new Coordinate(p.X, p.Y)).ToArray();
        if (!coords[0].Equals2D(coords[^1]))
        {
            coords = coords.Append(coords[0]).ToArray();
        }
        var ring = _geometryFactory.CreateLinearRing(coords);
        var polygon = _geometryFactory.CreatePolygon(ring);
        return Math.Abs(polygon.Area);
    }

    /// <summary>
    /// Bridge small gaps by merging line segments that have endpoints within gapTolerance distance
    /// Uses iterative merging to connect all segments that should be bridged
    /// </summary>
    public List<RawPath> BridgeGaps(List<RawPath> paths, double gapTolerance)
    {
        if (gapTolerance <= 0 || paths.Count == 0)
            return paths;

        _logger.LogInformation("NTS: Bridging gaps with tolerance {Tolerance} for {Count} paths",
            gapTolerance, paths.Count);

        try
        {
            // Work with a mutable list of paths
            var workingPaths = paths.Select(p => new RawPath
            {
                Points = new List<Point2D>(p.Points),
                LineWidth = p.LineWidth,
                IsStroked = p.IsStroked,
                IsFilled = p.IsFilled,
                SegmentCount = p.SegmentCount,
                PathLength = p.PathLength,
                PathType = p.PathType,
                ObjectIndex = p.ObjectIndex
            }).ToList();

            bool merged = true;
            int iterations = 0;
            int maxIterations = workingPaths.Count * 2; // Prevent infinite loops

            // Iteratively merge paths until no more merges are possible
            while (merged && iterations < maxIterations)
            {
                merged = false;
                iterations++;

                for (int i = 0; i < workingPaths.Count; i++)
                {
                    if (workingPaths[i].Points.Count < 2)
                        continue;

                    var path1 = workingPaths[i];
                    var start1 = path1.Points[0];
                    var end1 = path1.Points[path1.Points.Count - 1];

                    for (int j = i + 1; j < workingPaths.Count; j++)
                    {
                        if (workingPaths[j].Points.Count < 2)
                            continue;

                        var path2 = workingPaths[j];
                        var start2 = path2.Points[0];
                        var end2 = path2.Points[path2.Points.Count - 1];

                        // Check all endpoint combinations
                        if (TryMergePaths(path1, path2, start1, end1, start2, end2, gapTolerance, out var mergedPath))
                        {
                            workingPaths[i] = mergedPath;
                            workingPaths.RemoveAt(j);
                            merged = true;
                            break; // Restart search after merge
                        }
                    }

                    if (merged)
                        break; // Restart outer loop
                }
            }

            // Close paths that are almost closed (start and end points are very close)
            // This helps polygon reconstruction by ensuring paths form closed loops
            const double closeTolerance = 1.0; // Close paths if endpoints are within 1 unit
            foreach (var path in workingPaths)
            {
                if (path.Points.Count >= 3)
                {
                    var start = path.Points[0];
                    var end = path.Points[path.Points.Count - 1];
                    double distance = Math.Sqrt(Math.Pow(end.X - start.X, 2) + Math.Pow(end.Y - start.Y, 2));
                    
                    if (distance <= closeTolerance && distance > 0.001) // Almost closed but not exactly
                    {
                        // Close the path by adding the first point at the end (creates a closed ring)
                        path.Points.Add(new Point2D { X = start.X, Y = start.Y });
                        _logger.LogDebug("NTS: Closed path with {PointCount} points (endpoints were {Distance:F2} apart)", 
                            path.Points.Count, distance);
                    }
                }
            }

            // Update path metadata
            foreach (var path in workingPaths)
            {
                path.SegmentCount = path.Points.Count;
                path.PathLength = CalculatePathLength(path.Points);
            }

            _logger.LogInformation("NTS: Gap bridging complete after {Iterations} iterations: {Original} → {Merged} paths",
                iterations, paths.Count, workingPaths.Count);

            return workingPaths;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "NTS: Gap bridging failed, returning original paths");
            return paths;
        }
    }

    /// <summary>
    /// Try to merge two paths if their endpoints are within gapTolerance and collinear
    /// </summary>
    private bool TryMergePaths(RawPath path1, RawPath path2, Point2D start1, Point2D end1, 
        Point2D start2, Point2D end2, double gapTolerance, out RawPath mergedPath)
    {
        mergedPath = path1;

        // Check all 4 endpoint combinations
        // Case 1: end1 connects to start2
        if (ArePointsClose(end1, start2, gapTolerance) && 
            AreEndpointsCollinear(end1, path1.Points.Count > 1 ? path1.Points[path1.Points.Count - 2] : start1, 
                                 start2, path2.Points.Count > 1 ? path2.Points[1] : end2))
        {
            var mergedPoints = new List<Point2D>(path1.Points);
            // Add path2 points (skip first point if it's very close to end1)
            if (!ArePointsClose(end1, start2, 0.1))
            {
                mergedPoints.Add(start2);
            }
            for (int i = 1; i < path2.Points.Count; i++)
            {
                mergedPoints.Add(path2.Points[i]);
            }
            mergedPath = CreateMergedPath(path1, path2, mergedPoints);
            return true;
        }

        // Case 2: end1 connects to end2 (reverse path2)
        if (ArePointsClose(end1, end2, gapTolerance) && 
            AreEndpointsCollinear(end1, path1.Points.Count > 1 ? path1.Points[path1.Points.Count - 2] : start1, 
                                 end2, path2.Points.Count > 1 ? path2.Points[path2.Points.Count - 2] : start2))
        {
            var mergedPoints = new List<Point2D>(path1.Points);
            // Add path2 points in reverse (skip last point if it's very close to end1)
            if (!ArePointsClose(end1, end2, 0.1))
            {
                mergedPoints.Add(end2);
            }
            for (int i = path2.Points.Count - 2; i >= 0; i--)
            {
                mergedPoints.Add(path2.Points[i]);
            }
            mergedPath = CreateMergedPath(path1, path2, mergedPoints);
            return true;
        }

        // Case 3: start1 connects to start2 (reverse path1)
        if (ArePointsClose(start1, start2, gapTolerance) && 
            AreEndpointsCollinear(start1, path1.Points.Count > 1 ? path1.Points[1] : end1, 
                                 start2, path2.Points.Count > 1 ? path2.Points[1] : end2))
        {
            var mergedPoints = new List<Point2D>();
            // Add path1 points in reverse
            for (int i = path1.Points.Count - 1; i >= 0; i--)
            {
                mergedPoints.Add(path1.Points[i]);
            }
            // Add path2 points (skip first point if it's very close to start1)
            if (!ArePointsClose(start1, start2, 0.1))
            {
                mergedPoints.Add(start2);
            }
            for (int i = 1; i < path2.Points.Count; i++)
            {
                mergedPoints.Add(path2.Points[i]);
            }
            mergedPath = CreateMergedPath(path1, path2, mergedPoints);
            return true;
        }

        // Case 4: start1 connects to end2 (reverse path1)
        if (ArePointsClose(start1, end2, gapTolerance) && 
            AreEndpointsCollinear(start1, path1.Points.Count > 1 ? path1.Points[1] : end1, 
                                 end2, path2.Points.Count > 1 ? path2.Points[path2.Points.Count - 2] : start2))
        {
            var mergedPoints = new List<Point2D>();
            // Add path1 points in reverse
            for (int i = path1.Points.Count - 1; i >= 0; i--)
            {
                mergedPoints.Add(path1.Points[i]);
            }
            // Add path2 points (skip last point if it's very close to start1)
            if (!ArePointsClose(start1, end2, 0.1))
            {
                mergedPoints.Add(end2);
            }
            for (int i = path2.Points.Count - 2; i >= 0; i--)
            {
                mergedPoints.Add(path2.Points[i]);
            }
            mergedPath = CreateMergedPath(path1, path2, mergedPoints);
            return true;
        }

        return false;
    }

    /// <summary>
    /// Create a merged path from two paths
    /// </summary>
    private RawPath CreateMergedPath(RawPath path1, RawPath path2, List<Point2D> mergedPoints)
    {
        return new RawPath
        {
            Points = mergedPoints,
            LineWidth = Math.Max(path1.LineWidth, path2.LineWidth), // Use thicker line width
            IsStroked = path1.IsStroked || path2.IsStroked,
            IsFilled = path1.IsFilled || path2.IsFilled,
            SegmentCount = mergedPoints.Count,
            PathLength = CalculatePathLength(mergedPoints),
            PathType = "Polyline", // Merged paths are polylines
            ObjectIndex = Math.Min(path1.ObjectIndex, path2.ObjectIndex) // Keep original index
        };
    }

    /// <summary>
    /// Check if two points are close within tolerance
    /// </summary>
    private bool ArePointsClose(Point2D p1, Point2D p2, double tolerance)
    {
        double distance = Math.Sqrt(Math.Pow(p1.X - p2.X, 2) + Math.Pow(p1.Y - p2.Y, 2));
        return distance <= tolerance;
    }

    /// <summary>
    /// Check if endpoints can be bridged by extending lines in their current direction
    /// Rule: Only bridge if lines are collinear (nearly parallel) AND gap aligns with both directions
    /// This prevents bridging parallel lines at corners (like ||)
    /// </summary>
    private bool AreEndpointsCollinear(Point2D endpoint1, Point2D directionPoint1, Point2D endpoint2, Point2D directionPoint2)
    {
        // Calculate direction vectors for each line
        double dx1 = directionPoint1.X - endpoint1.X;
        double dy1 = directionPoint1.Y - endpoint1.Y;
        double dx2 = directionPoint2.X - endpoint2.X;
        double dy2 = directionPoint2.Y - endpoint2.Y;

        // Calculate gap direction
        double gapDx = endpoint2.X - endpoint1.X;
        double gapDy = endpoint2.Y - endpoint1.Y;

        // Normalize vectors
        double len1 = Math.Sqrt(dx1 * dx1 + dy1 * dy1);
        double len2 = Math.Sqrt(dx2 * dx2 + dy2 * dy2);
        double gapLen = Math.Sqrt(gapDx * gapDx + gapDy * gapDy);

        if (len1 < 0.001 || len2 < 0.001 || gapLen < 0.001)
            return true; // Degenerate case (points are very close), allow connection

        dx1 /= len1;
        dy1 /= len1;
        dx2 /= len2;
        gapDx /= gapLen;
        gapDy /= gapLen;

        // First check: Are the two lines collinear (nearly parallel and pointing in same direction)?
        // Use dot product to check if line directions are aligned
        double linesDot = Math.Abs(dx1 * dx2 + dy1 * dy2);
        if (linesDot < 0.95) // Lines are not collinear (more than ~18 degrees apart)
        {
            return false; // Don't bridge non-collinear lines
        }

        // Second check: Does the gap direction align with BOTH line directions?
        // The gap should be in the same direction as both lines (we can extend either line to bridge)
        double gapDot1 = gapDx * dx1 + gapDy * dy1;
        double gapDot2 = gapDx * dx2 + gapDy * dy2;

        // Gap must align with both directions (within ~18 degrees)
        // We use the absolute value to allow bridging in either direction along the line
        return Math.Abs(gapDot1) > 0.95 && Math.Abs(gapDot2) > 0.95;
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
    /// Recursively extract LineStrings from a geometry
    /// </summary>
    private void ExtractLineStrings(Geometry geometry, List<RawPath> result, double lineWidth)
    {
        if (geometry is LineString lineString)
        {
            var points = lineString.Coordinates.Select(c => new Point2D { X = c.X, Y = c.Y }).ToList();
            if (points.Count >= 2)
            {
                result.Add(new RawPath
                {
                    Points = points,
                    LineWidth = lineWidth,
                    PathLength = lineString.Length
                });
            }
        }
        else if (geometry is GeometryCollection collection)
        {
            foreach (var geom in collection.Geometries)
            {
                ExtractLineStrings(geom, result, lineWidth);
            }
        }
    }

    /// <summary>
    /// Reconstruct closed polygons from line segments using NTS Polygonizer
    /// This is MUCH faster and more robust than the custom implementation
    /// </summary>
    public List<RoomBoundary> ReconstructPolygons(List<RawPath> paths)
    {
        _logger.LogInformation("NTS: Reconstructing polygons from {Count} paths", paths.Count);

        if (paths.Count == 0)
        {
            _logger.LogWarning("NTS: No paths provided for polygon reconstruction");
            return new List<RoomBoundary>();
        }

        try
        {
            // Convert paths to NTS LineStrings
            var lineStrings = paths.Select(ToLineString).ToList();

            // Log path information for debugging
            int totalPoints = lineStrings.Sum(ls => ls.Coordinates.Length);
            _logger.LogDebug("NTS: Total points in all paths: {PointCount}", totalPoints);
            _logger.LogDebug("NTS: Path lengths: {Lengths}", 
                string.Join(", ", lineStrings.Select(ls => ls.Length.ToString("F2"))));

            // Use NTS Polygonizer to find all closed polygons
            var polygonizer = new Polygonizer();
            polygonizer.Add(lineStrings.Cast<Geometry>().ToList());

            // Get all results from polygonizer
            var polygons = polygonizer.GetPolygons();
            var cutEdges = polygonizer.GetCutEdges();
            var dangles = polygonizer.GetDangles();
            var invalidRingLines = polygonizer.GetInvalidRingLines();

            _logger.LogInformation("NTS: Polygonizer results - Polygons: {PolygonCount}, Cut Edges: {CutEdgeCount}, Dangles: {DangleCount}, Invalid Rings: {InvalidRingCount}",
                polygons.Count, cutEdges.Count, dangles.Count, invalidRingLines.Count);

            if (polygons.Count == 0)
            {
                _logger.LogWarning("NTS: No polygons found. This may indicate:");
                _logger.LogWarning("  - Paths do not form closed loops");
                _logger.LogWarning("  - Gaps are too large to form closed rings");
                _logger.LogWarning("  - Paths need additional gap bridging");
                
                // Log some diagnostic information
                if (cutEdges.Count > 0)
                {
                    _logger.LogDebug("NTS: Found {Count} cut edges (edges that are not part of any polygon)", cutEdges.Count);
                }
                if (dangles.Count > 0)
                {
                    _logger.LogDebug("NTS: Found {Count} dangles (edges that don't connect to form rings)", dangles.Count);
                }
            }

            // Convert to RoomBoundary
            var result = polygons
                .Cast<Polygon>()
                .Select(ToRoomBoundary)
                .ToList();

            _logger.LogInformation("NTS: Reconstructed {Count} polygons", result.Count);

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "NTS: Polygon reconstruction failed");
            return new List<RoomBoundary>();
        }
    }

    /// <summary>
    /// Remove outer polygons (keep only inner boundaries)
    /// Uses spatial containment checks - much faster than custom implementation
    /// </summary>
    public List<RoomBoundary> RemoveOuterPolygons(List<RoomBoundary> boundaries)
    {
        if (boundaries.Count <= 1)
            return boundaries;

        _logger.LogInformation("NTS: Removing outer polygons from {Count} boundaries", boundaries.Count);

        try
        {
            // Convert to NTS Polygons
            var ntsPolygons = boundaries.Select(b =>
            {
                var coords = b.Polygon.Select(p => new Coordinate(p.X, p.Y)).ToArray();
                // Ensure closed ring
                if (!coords[0].Equals2D(coords[^1]))
                {
                    coords = coords.Append(coords[0]).ToArray();
                }
                var ring = _geometryFactory.CreateLinearRing(coords);
                return _geometryFactory.CreatePolygon(ring);
            }).ToList();

            // Find polygons that are not contained by any other polygon
            var innerPolygons = new List<Polygon>();

            for (int i = 0; i < ntsPolygons.Count; i++)
            {
                bool isContained = false;

                for (int j = 0; j < ntsPolygons.Count; j++)
                {
                    if (i == j) continue;

                    // Check if polygon i is contained within polygon j
                    if (ntsPolygons[j].Contains(ntsPolygons[i]))
                    {
                        isContained = true;
                        break;
                    }
                }

                // Keep polygons that are NOT contained (inner boundaries)
                if (!isContained)
                {
                    innerPolygons.Add(ntsPolygons[i]);
                }
            }

            _logger.LogInformation("NTS: Removed outer polygons: {Original} → {Inner}",
                boundaries.Count, innerPolygons.Count);

            // Convert back to RoomBoundary
            return innerPolygons.Select(ToRoomBoundary).ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "NTS: Remove outer polygons failed, returning original");
            return boundaries;
        }
    }

    /// <summary>
    /// Remove nested polygons (keep only outermost polygons)
    /// </summary>
    public List<RoomBoundary> RemoveNestedPolygons(List<RoomBoundary> boundaries)
    {
        if (boundaries.Count <= 1)
            return boundaries;

        _logger.LogInformation("NTS: Removing nested polygons from {Count} boundaries", boundaries.Count);

        try
        {
            // Convert to NTS Polygons
            var ntsPolygons = boundaries.Select(b =>
            {
                var coords = b.Polygon.Select(p => new Coordinate(p.X, p.Y)).ToArray();
                if (!coords[0].Equals2D(coords[^1]))
                {
                    coords = coords.Append(coords[0]).ToArray();
                }
                var ring = _geometryFactory.CreateLinearRing(coords);
                return _geometryFactory.CreatePolygon(ring);
            }).ToList();

            // Keep only polygons that don't contain any other polygon
            var outerPolygons = new List<Polygon>();

            for (int i = 0; i < ntsPolygons.Count; i++)
            {
                bool containsOther = false;

                for (int j = 0; j < ntsPolygons.Count; j++)
                {
                    if (i == j) continue;

                    // Check if polygon i contains polygon j
                    if (ntsPolygons[i].Contains(ntsPolygons[j]))
                    {
                        containsOther = true;
                        break;
                    }
                }

                // Keep polygons that don't contain others
                if (!containsOther)
                {
                    outerPolygons.Add(ntsPolygons[i]);
                }
            }

            _logger.LogInformation("NTS: Removed nested polygons: {Original} → {Outer}",
                boundaries.Count, outerPolygons.Count);

            return outerPolygons.Select(ToRoomBoundary).ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "NTS: Remove nested polygons failed, returning original");
            return boundaries;
        }
    }

    /// <summary>
    /// Filter polygons by minimum area
    /// </summary>
    public List<RoomBoundary> FilterByMinArea(List<RoomBoundary> boundaries, double minArea)
    {
        if (minArea <= 0)
            return boundaries;

        var filtered = boundaries.Where(b => GetArea(b) >= minArea).ToList();

        _logger.LogInformation("NTS: Filtered by min area {MinArea}: {Original} → {Filtered}",
            minArea, boundaries.Count, filtered.Count);

        return filtered;
    }
}

