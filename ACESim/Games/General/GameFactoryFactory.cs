using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.ComponentModel.Composition;
using System.ComponentModel.Composition.Hosting;

namespace ACESim
{
    public class GameFactoryFactory
    {
        private CompositionContainer _container;

        [ImportMany]
        public IEnumerable<Lazy<IGameFactory, IGameName>> gameFactories;

        public GameFactoryFactory()
        {
            //An aggregate catalog that combines multiple catalogs
            var catalog = new AggregateCatalog();
            //Adds all the parts found in the same assembly as the GameContainer class
            catalog.Catalogs.Add(new AssemblyCatalog(typeof(GameFactoryFactory).Assembly));

            //Create the CompositionContainer with the parts in the catalog
            _container = new CompositionContainer(catalog);

            this._container.ComposeParts(this);
        }

        public IGameFactory GetGameFactory(string gameName)
        {
            try
            {
                return gameFactories.ToList().Single(x => x.Metadata.GameName.Equals(gameName)).Value;
            }
            catch(Exception ex)
            {
                throw new FileLoaderException(
                    "Could not find code for the game named " + gameName + ". Make sure that the game subclass includes the needed Export attributes.", 
                    ex);
            }
        }
    }
}
