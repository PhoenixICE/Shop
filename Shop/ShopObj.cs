using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Timers;

using TShockAPI;
using TShockAPI.DB;
using Terraria;

namespace Shop
{
    internal class ShopObj
    {
        public string Item;
        public int Price;
        public List<string> Region;
        public List<string> Group;
        public Timer RestockTimer;
        public int Stock;
        public List<string> Onsale;

        public ShopObj(string item, int price, List<string> regions, List<string> groups, int restockTimer, int stock, List<string> onsales)
        {
            this.Item = item;
            this.Price = price;
            this.Region = regions;
            this.Group = groups;
            if (restockTimer > 0)
            {
                this.RestockTimer = new Timer(restockTimer);
                this.RestockTimer.Elapsed += new ElapsedEventHandler(TimerElapsed);
                this.RestockTimer.Enabled = true; // Enable it
            }
            this.Stock = stock;
            this.Onsale = onsales;
        }

        private void TimerElapsed(object sender, ElapsedEventArgs e)
        {
            this.Stock += 1;
        }
    }
}
