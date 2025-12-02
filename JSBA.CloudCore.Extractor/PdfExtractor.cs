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
        /// </summary>
        public RoomsResult ProcessPdfToRooms(Stream pdfStream, PdfOptions options)
        {
            return _engine.ProcessPdfToRooms(pdfStream, options);
        }

        /// <summary>
        /// Compare different boundary extraction methods (for testing/diagnostics)
        /// </summary>
        public BoundaryComparisonResult CompareBoundaryExtractionMethods(Stream pdfStream)
        {
            return _engine.CompareBoundaryExtractionMethods(pdfStream);
        }

        /// <summary>
        /// Compare different text extraction methods (for testing/diagnostics)
        /// </summary>
        public TextComparisonResult CompareTextExtractionMethods(Stream pdfStream)
        {
            return _engine.CompareTextExtractionMethods(pdfStream);
        }
    }
}

