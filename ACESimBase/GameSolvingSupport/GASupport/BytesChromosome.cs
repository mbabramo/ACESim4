using ACESim;
using GeneticSharp.Domain.Chromosomes;
using GeneticSharp.Domain.Randomizations;
using GeneticSharp.Infrastructure.Framework.Commons;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace ACESim
{
    /// <summary>
    /// Floating point chromosome with binary values (0 and 1).
    /// </summary>
    public class BytesChromosome : BinaryChromosomeBase
    {
        private byte[] m_maxValue;
        private byte[] m_geneValues;
        private string m_originalValueStringRepresentation;

        public BytesChromosome(int length) : base(length)
        {

        }

        public BytesChromosome(byte[] maxValue, byte[] geneValues)
            : base(maxValue.Length)
        {
            Initialize(maxValue, geneValues);
        }

        public void Initialize(byte[] maxValue, byte[] geneValues)
        {
            m_maxValue = maxValue;

            // If values are not supplied, create random values
            if (geneValues == null)
            {
                geneValues = new byte[maxValue.Length];

                for (int i = 0; i < geneValues.Length; i++)
                {
                    geneValues[i] = (byte)(RandomGenerator.Next(maxValue[i]) + 1);
                }
            }
            else
                m_geneValues = geneValues.ToArray();

            m_originalValueStringRepresentation = String.Join(
                ",", geneValues);

            CreateGenes();
        }

        /// <summary>
        /// Creates the new.
        /// </summary>
        /// <returns>The new.</returns>
        public override IChromosome CreateNew()
        {
            return new BytesChromosome(m_maxValue, m_geneValues);
        }

        /// <summary>
        /// Generates the gene.
        /// </summary>
        /// <returns>The gene.</returns>
        /// <param name="geneIndex">Gene index.</param>
        public override Gene GenerateGene(int geneIndex)
        {
            return new Gene(m_geneValues[geneIndex]);
        }

        private byte EnsureMinMax(byte value, int index)
        {
            if (value < 1)
            {
                return 1;
            }

            if (value > m_maxValue[index])
            {
                return m_maxValue[index];
            }

            return value;
        }

        public override string ToString()
        {
            return m_originalValueStringRepresentation;
        }
    }
}
