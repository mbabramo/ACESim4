using System.Collections.Generic;
using System;

namespace ACESim
{
    public class ExecuteList
    {
        public List<CommandSet> CommandSets;

        public ExecuteList(List<CommandSet> commandSets)
        {
            CommandSets = commandSets;
        }
    }

}