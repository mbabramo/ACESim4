using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace ACESim
{
    /// <summary>
    /// Base class for all commands, which are always contained in multipart commands
    /// </summary>
    [Serializable]
    public abstract class Command
    {
        public DateTime CommandSetStartTime;

        /// <summary>
        /// The MultiPartCommand in which this Command is contained.
        /// </summary>
        public MultiPartCommand MultiPartCommand;

        public double CommandDifficulty;

        public Command(MultiPartCommand theMultipartCommand)
        {
            MultiPartCommand = theMultipartCommand;
        }

        [NonSerialized]
		[System.Xml.Serialization.XmlIgnore]
        public Dictionary<int, StoredSettingAndFieldInfo> storedSettingsInfo = null; // int is a hash code of the settings set

        /// <param name="simulationInteraction"></param>
        public abstract void Execute(SimulationInteraction simulationInteraction);
    }
}
