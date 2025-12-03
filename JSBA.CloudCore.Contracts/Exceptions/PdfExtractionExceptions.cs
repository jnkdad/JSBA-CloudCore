// PdfExtractionExceptions.cs
// Custom exceptions for PDF extraction API errors
// These exceptions map to the error codes defined in RoomExchange API v0.3

using System;

namespace JSBA.CloudCore.Contracts.Exceptions
{
    /// <summary>
    /// Base exception for PDF extraction errors
    /// </summary>
    public abstract class PdfExtractionException : Exception
    {
        /// <summary>
        /// Error code as defined in RoomExchange API v0.3
        /// </summary>
        public string ErrorCode { get; }

        protected PdfExtractionException(string errorCode, string message) : base(message)
        {
            ErrorCode = errorCode;
        }

        protected PdfExtractionException(string errorCode, string message, Exception innerException) 
            : base(message, innerException)
        {
            ErrorCode = errorCode;
        }
    }

    /// <summary>
    /// Exception thrown when no PDF file is provided (MISSING_FILE)
    /// </summary>
    public class MissingFileException : PdfExtractionException
    {
        public MissingFileException() 
            : base("MISSING_FILE", "No PDF file provided or file is empty.")
        {
        }
    }

    /// <summary>
    /// Exception thrown when the PDF file cannot be parsed (INVALID_PDF)
    /// </summary>
    public class InvalidPdfException : PdfExtractionException
    {
        public InvalidPdfException() 
            : base("INVALID_PDF", "The uploaded file is not a valid PDF document.")
        {
        }

        public InvalidPdfException(string message) 
            : base("INVALID_PDF", message)
        {
        }

        public InvalidPdfException(Exception innerException) 
            : base("INVALID_PDF", "The uploaded file could not be parsed as a PDF document.", innerException)
        {
        }
    }

    /// <summary>
    /// Exception thrown when PDF extraction fails (EXTRACTION_FAILED)
    /// </summary>
    public class ExtractionFailedException : PdfExtractionException
    {
        public ExtractionFailedException() 
            : base("EXTRACTION_FAILED", "An internal error occurred while processing the PDF.")
        {
        }

        public ExtractionFailedException(string message) 
            : base("EXTRACTION_FAILED", message)
        {
        }

        public ExtractionFailedException(Exception innerException) 
            : base("EXTRACTION_FAILED", "An internal error occurred while processing the PDF.", innerException)
        {
        }
    }

    /// <summary>
    /// Exception thrown when PDF contains unsupported structure (UNSUPPORTED_FORMAT)
    /// </summary>
    public class UnsupportedFormatException : PdfExtractionException
    {
        public UnsupportedFormatException() 
            : base("UNSUPPORTED_FORMAT", "The PDF contains an unsupported structure or format.")
        {
        }

        public UnsupportedFormatException(string message) 
            : base("UNSUPPORTED_FORMAT", message)
        {
        }

        public UnsupportedFormatException(Exception innerException) 
            : base("UNSUPPORTED_FORMAT", "The PDF contains an unsupported structure or format.", innerException)
        {
        }
    }
}

