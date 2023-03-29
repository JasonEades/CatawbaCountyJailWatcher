using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CatawbaCountyJailWatcher.Service
{
    public class Inmate
    {

        public string PictureId { get; set; }
        public string ImageUrl
        {
            get
            {
                return $"https://injail.catawbacountync.gov/WhosInJail/{PictureId}";
            }
        }

        public string Name { get; set; }
        public string Address { get; set; }
        public int Age { get; set; }
        public string Crime { get; set; }
        public DateTime? DateConfined { get; set; }
        public DateTime? CourtDate { get; set; }
        public string Bond { get; set; }
    }
}
