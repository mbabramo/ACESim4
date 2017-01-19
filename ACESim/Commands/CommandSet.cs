using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace ACESim
{
    /// <summary>
    /// The top-level container for <c>Command</c>s.  Contains <c>CommandSet</c>s, which in turn contain <c>Command</c>s.
    /// </summary>
    public class CommandSet
    {
        public string Name;
        public List<MultiPartCommand> Commands;

        public double CommandDifficulty { get { if (Commands == null) return 0; return Commands.Sum(x => x.CommandDifficulty); } }

        public CommandSet(string name)
        {
            Name = name;
            Commands = new List<MultiPartCommand>();
        }
    }
}
