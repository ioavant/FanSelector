using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TES_test2;

public class CsvReader
{
    public static List<ElementParameter> ReadCsv(string filePath)
    {
        var parameters = new List<ElementParameter>();
        var lines = File.ReadAllLines(filePath).Skip(1); // Skip header line
        foreach (var line in lines)
        {
            var values = line.Split(',');
            var parameter = new ElementParameter
            {
                Mark = values[0],
                Size = values[1],
                AirFlow = int.Parse(values[2]),
                Pressure = int.Parse(values[3]),
                Power = double.Parse(values[4]),
                RPM = int.Parse(values[5])
            };
            parameters.Add(parameter);
        }
        return parameters;
    }
}
