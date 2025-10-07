using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ACESimBase.Util.Collections
{
    using System;
    using System.Diagnostics;

    // DEBUG -- delete this class

    public sealed class Interceptable2DArray<T>
{
    private readonly T[,] storage;

    public int RowCount => storage.GetLength(0);
    public int ColumnCount => storage.GetLength(1);
        public int ID;

    public Interceptable2DArray(int rows, int columns)
    {
        if (rows < 0) throw new ArgumentOutOfRangeException(nameof(rows));
        if (columns < 0) throw new ArgumentOutOfRangeException(nameof(columns));
        storage = new T[rows, columns];
    }

    public Interceptable2DArray(T[,] existing)
    {
        if (existing == null) throw new ArgumentNullException(nameof(existing));
        storage = (T[,])existing.Clone();
    }

    public T this[int row, int column]
    {
        get
        {
            // Intercept read
            var value = storage[row, column];
            return value;
        }
        set
        {
                if (row == 0 && ID == 0)
                {
                    var DEBUG = 0;
                    Debug.WriteLine($"Set {column} to {value} at {counter++}");
                }
            // Intercept write
            storage[row, column] = value;
        }
    }

        static int counter;

    public T[,] ToArrayCopy() => (T[,])storage.Clone();
}


}
