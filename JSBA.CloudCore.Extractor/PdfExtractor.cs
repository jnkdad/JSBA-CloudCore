using System.Collections.Generic;
using System.Threading.Tasks;

namespace JSBA.CloudCore.Extractor;

public record SamplePoint(double X, double Y);
public record SampleRoom(string Id, string Name, IReadOnlyList<SamplePoint> Polygon);

public class PdfExtractor
{
    public Task<IReadOnlyList<SampleRoom>> GetSampleRoomsAsync()
    {
        var rooms = new List<SampleRoom>
        {
            new(
                Id: "01-101",
                Name: "CLASSROOM",
                Polygon: new List<SamplePoint>
                {
                    new(0, 0),
                    new(34.46, 0),
                    new(34.46, 27.57),
                    new(0, 27.57)
                }
            ),
            new(
                Id: "01-102",
                Name: "LAB",
                Polygon: new List<SamplePoint>
                {
                    new(40, 0),
                    new(70, 0),
                    new(70, 25),
                    new(40, 25)
                }
            )
        };

        return Task.FromResult<IReadOnlyList<SampleRoom>>(rooms);
    }
}
