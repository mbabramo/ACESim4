using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ACESimBase.GameSolvingSupport.ECTAAlgorithm
{
    public struct Rat
    {
        private int _num, _den;
        public int num
        {
            get
            {
                return _num;
            }
            set
            {
                _num = value;
                CheckForMatchDEBUG();
            }
        }
        public int den
        {
            get
            {
                return _den;
            }
            set
            {
                _den = value; 
                CheckForMatchDEBUG();
            }
        }

        public override string ToString()
        {
            string s = num.ToString();
            if (den != 1)
                return s + "/" + den.ToString();
            return s;
        }

        public void CheckForMatchDEBUG()
        {
            if (_num == 10643 && _den == 162)
            {
                var DEBUG = 0;
            }
        }
    }
}
