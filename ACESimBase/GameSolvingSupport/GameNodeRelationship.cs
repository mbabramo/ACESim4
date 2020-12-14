﻿using ACESim;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ACESimBase.GameSolvingSupport
{
    public record GameNodeRelationship(int NodeID, IGameState GameState, int? ParentNodeID, byte? ActionAtParent)
    {
        public int PlayerID => GameState switch
        {
            ChanceNode c => c.PlayerNum,
            InformationSetNode i => i.PlayerIndex,
            _ => -1
        };

        public override string ToString()
        {
            return $"{NodeID} (P{ParentNodeID} -> {ActionAtParent}): {GameState}";
        }
    }
}
