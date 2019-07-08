using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace ACESim
{
    [Serializable]
    public class InformationSetNodesCoreData
    {
        public InformationSetNodeCoreData[] Data;

        public InformationSetNodesCoreData(IEnumerable<InformationSetNode> informationSets)
        {
            Data = informationSets.Select(x => new InformationSetNodeCoreData(x)).ToArray();
        }

        public void CopyToInformationSets(List<InformationSetNode> informationSets)
        {
            for (int i = 0; i < Data.Length; i++)
            {
                InformationSetNode informationSetNode = informationSets[i];
                if (Data[i].InformationSetNodeNumber != informationSetNode.InformationSetNodeNumber)
                    throw new Exception();
                Data[i].CopyToInformationSet(informationSetNode);
            }
        }
    }
}
