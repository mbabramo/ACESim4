using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.ComponentModel.Composition;
using System.ComponentModel.Composition.Hosting;

namespace ACESim
{
    public class DistributionFactory
    {
        private CompositionContainer _container;

        [ImportMany]
        public IEnumerable<Lazy<IDistribution, IDistributionName>> distributionFactories;

        public DistributionFactory()
        {
            //An aggregate catalog that combines multiple catalogs
            var catalog = new AggregateCatalog();

            //Adds all the parts found in the same assembly as the DistributionFactory class
            catalog.Catalogs.Add(new AssemblyCatalog(typeof(DistributionFactory).Assembly));

            //Create the CompositionContainer with the parts in the catalog
            _container = new CompositionContainer(catalog);

            this._container.ComposeParts(this);
        }

        public IDistribution GetDistribution(string distributionName)
        {
            try
            {
                return distributionFactories.ToList().Single(x => x.Metadata.DistributionName.Equals(distributionName)).Value;
            }
            catch (Exception ex)
            {
                throw new FileLoaderException(
                    "Could not find code for the distribution named " + distributionName + ". Make sure that the distribution subclass includes the needed Export attributes.",
                    ex);
            }
        }
    }
}
