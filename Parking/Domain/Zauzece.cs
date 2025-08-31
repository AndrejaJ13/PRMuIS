using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.ConstrainedExecution;

namespace Domain
{
    [Serializable]
    public class Zauzece
    {
        public int BrojParkinga { get; set; }
        public int BrojMesta { get; set; }
        public string VremeNapustanja { get; set; }
        public DateTime VremeDolaska { get; set; } = DateTime.Now;
        public List<Car> Cars { get; set; } = new List<Car>();

        public override string ToString()
        {
            var baseString = $"Parking: {BrojParkinga}\nBroj mesta: {BrojMesta}\nVreme napustanja: {VremeNapustanja}\nVreme dolaska: {VremeDolaska}";

            if (Cars != null && Cars.Any())
            {
                baseString += "\nVozila:";
                foreach (var car in Cars)
                {
                    baseString += $"\n  {car}";
                }
            }

            return baseString;
        }
    }
}