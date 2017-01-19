﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace ACESim
{
    public static class ArrayFormConversionExtension
    {
        public static double[][] ConvertToArrayForm(this IEnumerable<double> items)
        {
            return items.Select(x => new double[] { x }).ToArray();
        }
        
        public static double[][] ConvertToArrayFormTrainingHalf(this IEnumerable<double> items)
        {
            if (items.Count() % 2 == 0)
                return ConvertToArrayFormEvenNumberedItems(items); 
            else
                return ConvertToArrayFormOddNumberedItems(items);
        }

        public static double[][] ConvertToArrayFormValidationHalf(this IEnumerable<double> items)
        {
            if (items.Count() % 2 == 1)
                return ConvertToArrayFormEvenNumberedItems(items);
            else
                return ConvertToArrayFormOddNumberedItems(items);
        }

        public static double[][] ConvertToArrayFormEvenNumberedItems(this IEnumerable<double> items)
        {
            return items.EvenNumberedItems().Select(x => new double[] { x }).ToArray();
        }

        public static double[][] ConvertToArrayFormOddNumberedItems(this IEnumerable<double> items)
        {
            return items.OddNumberedItems().Select(x => new double[] { x }).ToArray();
        }

        public static List<double> EvenNumberedItems(this IEnumerable<double> items)
        {
            return items.Select((item, index) => new { Item = item, Index = index }).Where(x => x.Index % 2 == 0).Select(x => x.Item).ToList();
        }

        public static List<double> OddNumberedItems(this IEnumerable<double> items)
        {
            return items.Select((item, index) => new { Item = item, Index = index }).Where(x => x.Index % 2 != 0).Select(x => x.Item).ToList();
        }
    }
}
