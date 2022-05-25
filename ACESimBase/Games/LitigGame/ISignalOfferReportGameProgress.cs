using System.Collections.Generic;
using System.Linq;

namespace ACESim
{
    public interface ISignalOfferReportGameProgress
    {
        bool PFiles { get; }
        bool DAnswers { get; }
        List<double> POffers { get; }
        List<double> DOffers { get; }

        double PFirstOffer => POffers.First();
        double DFirstOffer => DOffers.First();
    }
}
