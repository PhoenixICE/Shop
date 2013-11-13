using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using TShockAPI;
using TShockAPI.DB;
using Terraria;
using Newtonsoft.Json;
using System.Timers;

namespace Shop
{
    internal class ShopData
    {
        private Shop main;
        private List<ShopObj> Shoplist = new List<ShopObj>();
        private List<Timer> timer = new List<Timer>();

        public ShopObj FindShopObjbyItemName(string name)
        {
            foreach (ShopObj obj in Shoplist)
            {
                if (obj.Item.ToLower() == name.ToLower())
                {
                    return obj;
                }
            }
            return null;
        }

        public void lowerStock(ShopObj obj)
        {
            if (obj.Stock > 0)
            {
                obj.Stock -= 1;
                main.Database.Query("UPDATE storeshop SET stock = @0 WHERE name = @1 AND price = @2", obj.Stock, obj.Item, obj.Price);
            }
        }

        public void updateShopData()
        {
            try
            {
                using (QueryResult result = main.Database.QueryReader("SELECT * FROM storeshop"))
                {
                    while (result.Read())
                    {
                        this.Shoplist.Add(new ShopObj(result.Get<string>("name"), result.Get<int>("price"), result.Get<string>("region").Split(new Char[] { ',' }).ToList(), result.Get<string>("groupname").Split(new Char[] { ',' }).ToList(), result.Get<int>("restockTimer"), result.Get<int>("stock"), result.Get<string>("onsale").Split(new Char[] { ',' }).ToList()));
                    }
                }
            }
            catch (Exception e)
            {
                Log.ConsoleError(e.ToString());
            }
        }
        public ShopData(Shop instance)
        {
            main = instance;
            updateShopData();
        }

    }
}
