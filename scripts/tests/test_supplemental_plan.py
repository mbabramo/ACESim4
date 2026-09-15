import importlib.util
import itertools
import unittest
from pathlib import Path

spec=importlib.util.spec_from_file_location('supplemental',Path(__file__).parents[1]/'rebuild_article_supplemental.py')
module=importlib.util.module_from_spec(spec);spec.loader.exec_module(module)

class DirectedPlanTests(unittest.TestCase):
    def cases(self,risks,costs):
        return [dict(id=f'{c}-{r}-{f}',cost=c,alpha=r,fee=f)
                for c,r,f in itertools.product(costs,risks,['american','trial','complete'])]
    def test_core_has_every_direction_at_each_cost(self):
        pairs=module.contrasts(self.cases(['0','2'],['0.25','0.5','1','2','4']))
        self.assertEqual(len(pairs),90)
        ids={(a['id'],b['id']) for a,b in pairs}
        self.assertEqual(len(ids),90)
        self.assertTrue(all((b,a) in ids for a,b in ids))
        self.assertTrue(all(a['cost']==b['cost'] and ((a['alpha']==b['alpha']) != (a['fee']==b['fee'])) for a,b in pairs))
    def test_additional_risk_levels_are_discovered_without_new_pair_lists(self):
        pairs=module.contrasts(self.cases(['0','1','2'],['1']))
        self.assertEqual(len(pairs),36) # 3*6 fee directions + 3*6 preference directions

if __name__=='__main__':unittest.main()
