using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Domain
{
    [Serializable]
    public class Car
    {
        public string Manufacturer { get; set; }
        public string Model { get; set; }
        public string Color { get; set; }
        public string LicensePlate { get; set; }

        public override string ToString()
        {
            return $"{Manufacturer} {Model} {Color} {LicensePlate}";
        }
    }
}
