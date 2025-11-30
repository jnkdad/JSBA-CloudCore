using System.Threading;
using System.Threading.Tasks;

namespace JSBA.CloudCore.Contracts
{
    public interface IExtractor
    {
        Task<ExtractionResult> FromPdfAsync(ExtractionRequest req, CancellationToken ct);
        // future: FromDxfAsync, FromRevitAsync, etc.
    }

    public interface IConverter
    {
        Task<ConvertResult> ToEaseAsync(ConvertRequest req, CancellationToken ct);
        Task<ConvertResult> ToDxfAsync(ConvertRequest req, CancellationToken ct);
    }

    public interface IBuilder
    {
        Task<BuildResult> RevitPackageAsync(BuildRequest req, CancellationToken ct);
    }
}
