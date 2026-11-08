from pathlib import Path
import sys
import unittest

sys.path.insert(0,str(Path(__file__).resolve().parents[1]/'Scripts'))
from audit_endogenous_traditional import SeededRandom,money_gain,archive_rows


class IndependentAudit(unittest.TestCase):
    def test_compatibility_random_sequence(self):
        r=SeededRandom(0)
        self.assertAlmostEqual(r.next(),0.7262432699679598,places=16)
        self.assertAlmostEqual(r.next(),0.8173253595909687,places=16)
        self.assertNotEqual(SeededRandom(1729).next(),SeededRandom(2718).next())

    def test_affine_cara_inverse_and_domain(self):
        from decimal import Decimal,localcontext
        with localcontext() as c:
            c.prec=60;a=Decimal(2);amount=Decimal('.0002');scale=10/((2*a).exp()-1)
            g=scale*(1-(-a*amount).exp())
        self.assertAlmostEqual(money_gain(1000.,float(g),2),.0002,places=13)
        self.assertEqual(money_gain(10.,1e-6,0),1e-6)
        self.assertIsNone(money_gain(1000.,1.,2))

    def test_archived_probabilities_must_be_complete_and_normalized(self):
        a={'Keys':['0/x','1/y'],'Policies':[{'Player':0,'Probabilities':[[.2,.8]]},{'Player':1,'Probabilities':[[1.]]}]}
        self.assertEqual(archive_rows(a),[[.2,.8],[1.]])
        a['Policies'][0]['Probabilities'][0]=[.2,.7]
        with self.assertRaises(ValueError):archive_rows(a)


if __name__=='__main__':unittest.main()
