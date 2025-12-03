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
        /// <returns>RoomExchange response with rooms</returns>
        RoomsResponseDto ProcessPdfToRooms(Stream pdfStream, PdfOptions options);
    }
}

