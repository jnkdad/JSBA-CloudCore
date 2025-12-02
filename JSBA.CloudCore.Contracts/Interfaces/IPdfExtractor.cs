// IPdfExtractor.cs
// Interface for PDF extraction pipelines (vector, raster, etc.)

using JSBA.CloudCore.Contracts.Models;

namespace JSBA.CloudCore.Contracts.Interfaces
{
    /// <summary>
    /// Interface for PDF extraction pipelines.
    /// Implementations can be vector-based, raster-based, or hybrid.
    /// </summary>
    public interface IPdfExtractor
    {
        /// <summary>
        /// Extract rooms from a PDF stream
        /// </summary>
        /// <param name="pdfStream">PDF file stream</param>
        /// <param name="options">Extraction options</param>
        /// <returns>Extraction result with rooms and metadata</returns>
        RoomsResult ProcessPdfToRooms(Stream pdfStream, PdfOptions options);

        /// <summary>
        /// Compare different boundary extraction methods (for testing/diagnostics)
        /// </summary>
        /// <param name="pdfStream">PDF file stream</param>
        /// <returns>Comparison result showing boundaries from different methods</returns>
        BoundaryComparisonResult CompareBoundaryExtractionMethods(Stream pdfStream);

        /// <summary>
        /// Compare different text extraction methods (for testing/diagnostics)
        /// </summary>
        /// <param name="pdfStream">PDF file stream</param>
        /// <returns>Comparison result showing text labels from different methods</returns>
        TextComparisonResult CompareTextExtractionMethods(Stream pdfStream);
    }
}

