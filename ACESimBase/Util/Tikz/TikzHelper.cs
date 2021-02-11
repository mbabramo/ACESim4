﻿using ACESim;
using System.Collections.Generic;
using System.Linq;

namespace ACESimBase.Util.Tikz
{
    public static class TikzHelper
    {
        static double[] relativeWidthsComputerModernFont = new double[255] { 4.625108242034912, 15.496148109436035, 12.42404556274414, 15.496148109436035, 15.496148109436035, 15.496148109436035, 15.496148109436035, 15.496148109436035, 15.496148109436035, 60.1806640625, 4.625108242034912, 4.625108242034912, 16.94742774963379, 4.625108242034912, 15.496148109436035, 15.496148109436035, 15.496148109436035, 15.496148109436035, 15.496148109436035, 15.496148109436035, 15.496148109436035, 15.496148109436035, 15.496148109436035, 15.496148109436035, 15.496148109436035, 15.496148109436035, 15.496148109436035, 15.496148109436035, 4.625108242034912, 4.625108242034912, 4.625108242034912, 4.625108242034912, 4.629629135131836, 8.603694915771484, 11.78430461883545, 16.538265228271484, 11.78430461883545, 16.538265228271484, 15.744808197021484, 8.603694915771484, 10.177045822143555, 10.177045822143555, 11.78430461883545, 15.758371353149414, 8.590131759643555, 9.383588790893555, 8.603694915771484, 11.78430461883545, 11.78430461883545, 11.78430461883545, 11.78430461883545, 11.78430461883545, 11.78430461883545, 11.78430461883545, 11.78430461883545, 11.78430461883545, 11.78430461883545, 11.78430461883545, 8.590131759643555, 8.603694915771484, 15.758371353149414, 15.758371353149414, 15.758371353149414, 11.37740421295166, 15.744808197021484, 15.35825252532959, 14.74790096282959, 14.951351165771484, 15.54135799407959, 14.354562759399414, 13.95444393157959, 15.839752197265625, 15.35825252532959, 9.79049015045166, 11.960628509521484, 15.744808197021484, 13.567888259887695, 17.725059509277344, 15.35825252532959, 15.744808197021484, 14.354562759399414, 15.744808197021484, 15.154802322387695, 12.56419849395752, 14.951351165771484, 15.35825252532959, 15.35825252532959, 19.318754196166992, 15.35825252532959, 15.35825252532959, 13.37121868133545, 8.603694915771484, 11.78430461883545, 8.603694915771484, 13.37121868133545, 15.758371353149414, 11.78430461883545, 11.78430461883545, 12.56419849395752, 10.97728443145752, 12.56419849395752, 10.97728443145752, 8.990251541137695, 11.78430461883545, 12.56419849395752, 8.590131759643555, 8.990251541137695, 12.164079666137695, 8.590131759643555, 16.538265228271484, 12.56419849395752, 11.78430461883545, 12.56419849395752, 12.164079666137695, 10.21773624420166, 10.25842571258545, 10.177045822143555, 12.56419849395752, 12.164079666137695, 14.951351165771484, 12.164079666137695, 12.164079666137695, 10.97728443145752, 11.78430461883545, 8.603694915771484, 11.78430461883545, 13.37121868133545, 12.491862297058105, 15.496148109436035, 15.496148109436035, 15.496148109436035, 15.496148109436035, 4.625108242034912, 4.625108242034912, 15.496148109436035, 15.496148109436035, 15.496148109436035, 15.496148109436035, 15.496148109436035, 15.496148109436035, 15.496148109436035, 15.496148109436035, 15.496148109436035, 15.496148109436035, 15.496148109436035, 15.496148109436035, 15.496148109436035, 15.496148109436035, 15.496148109436035, 15.496148109436035, 15.496148109436035, 15.496148109436035, 15.496148109436035, 15.496148109436035, 15.496148109436035, 15.496148109436035, 15.496148109436035, 15.496148109436035, 15.496148109436035, 15.496148109436035, 9.383588790893555, 8.603694915771484, 11.78430461883545, 13.764556884765625, 13.373480796813965, 15.35825252532959, 8.71446418762207, 10.97728443145752, 11.78430461883545, 20.51911163330078, 10.97728443145752, 12.56419849395752, 14.171457290649414, 4.625108242034912, 20.51911163330078, 11.78430461883545, 9.383588790893555, 15.758371353149414, 9.867350578308105, 9.867350578308105, 11.78430461883545, 12.56419849395752, 13.37121868133545, 8.603694915771484, 10.97728443145752, 9.867350578308105, 10.97728443145752, 12.56419849395752, 17.74766731262207, 17.74766731262207, 17.74766731262207, 11.37740421295166, 15.35825252532959, 15.35825252532959, 15.35825252532959, 15.35825252532959, 15.35825252532959, 15.35825252532959, 17.541954040527344, 14.951351165771484, 14.354562759399414, 14.354562759399414, 14.354562759399414, 14.354562759399414, 9.79049015045166, 9.79049015045166, 9.79049015045166, 9.79049015045166, 15.55492115020752, 15.35825252532959, 15.744808197021484, 15.744808197021484, 15.744808197021484, 15.744808197021484, 15.744808197021484, 13.814290046691895, 15.758371353149414, 15.35825252532959, 15.35825252532959, 15.35825252532959, 15.35825252532959, 15.35825252532959, 13.567888259887695, 11.78430461883545, 11.78430461883545, 11.78430461883545, 11.78430461883545, 11.78430461883545, 11.78430461883545, 11.78430461883545, 14.951351165771484, 10.97728443145752, 10.97728443145752, 10.97728443145752, 10.97728443145752, 10.97728443145752, 8.590131759643555, 8.590131759643555, 8.590131759643555, 9.383588790893555, 11.78430461883545, 12.56419849395752, 11.78430461883545, 11.78430461883545, 11.78430461883545, 11.78430461883545, 11.78430461883545, 15.758371353149414, 11.78430461883545, 12.56419849395752, 12.56419849395752, 12.56419849395752, 12.56419849395752, 12.164079666137695, 12.56419849395752 };

        public static double RelativeCharWidth(char c)
        {
            if (c >= 0 && c <= 255)
                return relativeWidthsComputerModernFont[(byte)c];
            return 12.0; // guess
        }

        public static double ApproximateStringWidth(string s, double pointSize)
        {
            return s.ToCharArray().Sum(x => RelativeCharWidth(x)) * (pointSize / 10.0) * 1.2 / 69.0; // gives rough approximation in cm.
        }

        public static string GetStandaloneDocument(string contents, List<string> additionalPackages = null)
        {
            string packagesString = ""; 
            if (additionalPackages != null)
                packagesString = "\r\n" + string.Join("\r\n", additionalPackages.Select(p => $"\\usepackage{{{p}}}"));
            return $@"\documentclass{{standalone}}
\usepackage{{tikz}}{packagesString}
\usetikzlibrary{{patterns, positioning}}
\begin{{document}}
\begin{{tikzpicture}}
{contents}
\end{{tikzpicture}}
\end{{document}}";
        }

        public static string DrawText(double x, double y, string text, string attributes = "black, very thin")
        {
            return $"\\node[{attributes}] at ({x.ToSignificantFigures(3)}, {y.ToSignificantFigures(3)}) {{{text}}};";
        }
    }
}
