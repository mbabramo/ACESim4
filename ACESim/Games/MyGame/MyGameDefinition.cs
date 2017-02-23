using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.ComponentModel.Composition;

namespace ACESim
{
    [Export(typeof(ICodeBasedSettingGenerator))] // include the export and exportmetadata attributes if we implement GenerateSetting, setting up the GameModule by code instead of in the settings file.
    [ExportMetadata("CodeGeneratorName", "MyGameDefinition")]
    public class MyGameDefinition : GameDefinition, ICodeBasedSettingGenerator, ICodeBasedSettingGeneratorName
    {
        public string CodeGeneratorName => "MyGameDefinition";

        public object GenerateSetting(string options)
        {
            return new MyGameDefinition()
            {
                Players = new List<PlayerInfo>
                {
                    new PlayerInfo("C",(int) MyGamePlayers.Chance,true,false),
                    new PlayerInfo("P",(int) MyGamePlayers.Plaintiff,false,true),
                    new PlayerInfo("D",(int) MyGamePlayers.Defendant,false,true),
                },
                DecisionsExecutionOrder = new List<Decision>()
                {
                    new Decision("LitigationQuality", "Qual", (byte) MyGamePlayers.Chance, new List<byte> { }, 10),
                    new Decision("PlaintiffSignal", "PSig", (byte) MyGamePlayers.Chance, new List<byte> { }, 10),
                    new Decision("DefendantSignal", "DSig", (byte) MyGamePlayers.Chance, new List<byte> { }, 10),
                    new Decision("PlaintiffOffer", "PO", (byte) MyGamePlayers.Plaintiff, new List<byte> { (byte) MyGamePlayers.Plaintiff, (byte) MyGamePlayers.Defendant }, 10),
                    new Decision("DefendantOffer", "DO", (byte) MyGamePlayers.Defendant, new List<byte> { (byte) MyGamePlayers.Plaintiff, (byte) MyGamePlayers.Defendant }, 10),
                }
            };
        }

        
    }
}
