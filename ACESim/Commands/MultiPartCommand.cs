using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace ACESim
{
    /// <summary>
    /// Contains <c>Command</c>s and contained in <c>CommandSet</c>s.
    /// </summary>
    [Serializable]
    public class MultiPartCommand
    {
        public string Name;
        public string GameInputsName;
        public List<Command> Commands;
        public GameDefinitionSettingsSet GameDefinitionSet;

        public double CommandDifficulty { get { if (Commands == null) return 0; return Commands.Sum(x => x.CommandDifficulty); } }

        public MultiPartCommand()
        {
        }

        public MultiPartCommand(
            string name, 
            string gameInputsName, 
            List<Command> commands, 
            GameDefinitionSettingsSet gameDefinitionSet)
        {
            Name = name;
            GameInputsName = gameInputsName;
            Commands = commands;
            GameDefinitionSet = gameDefinitionSet;
        }
    }
}
