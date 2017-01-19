using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.ComponentModel.Composition;
using System.ComponentModel.Composition.Hosting;

namespace ACESim
{
    [Serializable]
    public class CodeBasedSettingGeneratorFactory
    {
        [NonSerialized]
		[System.Xml.Serialization.XmlIgnore]
         private CompositionContainer _container;

        [NonSerialized]
		[System.Xml.Serialization.XmlIgnore]
        [ImportMany]
         public IEnumerable<Lazy<ICodeBasedSettingGenerator, ICodeBasedSettingGeneratorName>> codeGenerators;

        public CodeBasedSettingGeneratorFactory()
        {
            //An aggregate catalog that combines multiple catalogs
            var catalog = new AggregateCatalog();
            //Adds all the parts found in the same assembly as the GameContainer class
            catalog.Catalogs.Add(new AssemblyCatalog(typeof(GameFactoryFactory).Assembly));

            //Create the CompositionContainer with the parts in the catalog
            _container = new CompositionContainer(catalog);

            this._container.ComposeParts(this);
        }

        public ICodeBasedSettingGenerator GetCodeGenerator(string codeGeneratorName)
        {
            try
            {
                return codeGenerators.ToList().Single(x => x.Metadata.CodeGeneratorName.Equals(codeGeneratorName)).Value;
            }
            catch(Exception ex)
            {
                throw new FileLoaderException(
                    "Could not find code generator named " + codeGeneratorName + ". Make sure that the code generator class includes the needed Export attributes.", 
                    ex);
            }
        }
    }
}
