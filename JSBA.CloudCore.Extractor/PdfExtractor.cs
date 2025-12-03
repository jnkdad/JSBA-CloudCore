// ExtractorFacade.cs
// Facade for vector PDF extraction pipeline
// Implements IPdfExtractor interface from CloudCore.Domain

using JSBA.CloudCore.Contracts.Interfaces;
using JSBA.CloudCore.Contracts.Models;
using Microsoft.Extensions.Logging;

namespace JSBA.CloudCore.Extractor
{
    /// <summary>
    /// Facade for vector-based PDF extraction.
    /// This is the public interface that the API layer will use.
    /// All PDF parsing logic is encapsulated within this module.
    /// </summary>
    public class PdfExtractor : IPdfExtractor
    {
        private readonly ExtractionEngine _engine;

        public PdfExtractor(ILogger<ExtractionEngine> logger)
        {
            // Create the extraction engine with the injected logger
            _engine = new ExtractionEngine(logger);
        }

        /// <summary>
        /// Extract rooms from a PDF stream
        /// Maps internal models to RoomExchange DTOs (RIMJSON v0.3)
        /// </summary>
        public RoomsResponseDto ProcessPdfToRooms(Stream pdfStream, PdfOptions options)
        {
            // Get internal result
            var internalResult = _engine.ProcessPdfToRooms(pdfStream, options);
            
            // Map to public DTO (RIMJSON v0.3)
            return RoomDtoMapper.MapToResponseDto(
                internalResult, 
                options.FileName ?? "unknown.pdf",
                options.PageIndex ?? 0,
                options.UnitsHint ?? "feet");
        }
    }
}

