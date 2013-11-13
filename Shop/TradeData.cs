using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using TShockAPI;
using TShockAPI.DB;
using Terraria;
using Newtonsoft.Json;

namespace Shop
{
    internal class TradeData
    {

        private Shop main;
        public List<TradeObj> tradeObj = new List<TradeObj>();
        public List<OfferObj> offerObj = new List<OfferObj>();
        public int tradeID = 0;
        public int offerID = 0;

        //adds new trade entry, and updates interal cached data.
        public void addTrade(string user, int itemid, int stack, int witemid = 0, int wstack = 0)
        {
            main.Database.Query("INSERT INTO storetrade(ID, User, ItemID, Stack, WItemID, WStack) VALUES(@0,@1,@2,@3,@4,@5)",tradeID,user,itemid,stack,witemid,wstack);
            this.tradeObj.Add(new TradeObj(tradeID, user, itemid, stack, witemid, wstack));
            tradeID += 1;
        }

        public TradeObj TradeObjByID(int ID)
        {
            foreach(TradeObj obj in tradeObj)
            {
                if (obj.ID == ID)
                {
                    return obj;
                }
            }
            return null;
        }

        public OfferObj OfferObjByID(int ID)
        {
            foreach (OfferObj obj in offerObj)
            {
                if (obj.ID == ID)
                {
                    return obj;
                }
            }
            return null;
        }

        public void processTrade(TSPlayer player, TradeObj obj, Item item, int stack)
        {
            main.Database.Query("DELETE FROM storetrade WHERE ID = @0", obj.ID);            
            main.Database.Query("INSERT INTO storeoffer(ID, User, ItemID, Stack, TradeID) VALUES(@0,@1,@2,@3,@4)", offerID, obj.User, item.netID, stack, -1);
            tradeObj.Remove(obj);
            offerObj.Add(new OfferObj(offerID, obj.User, item.netID, stack, -1));
            offerID += 1;            
        }

        public void processOffer(TSPlayer player, int id, int itemid, int stack)
        {
            main.Database.Query("INSERT INTO storeoffer(ID, User, ItemID, Stack, TradeID) VALUES(@0,@1,@2,@3,@4)", offerID, player.Name, itemid, stack, id);
            offerObj.Add(new OfferObj(offerID, player.Name, itemid, stack, id));
            offerID += 1;
        }

        internal void processAccept(TSPlayer player, OfferObj obj)
        {
            //remove offer
            main.Database.Query("DELETE FROM storeoffer WHERE ID = @0", obj.ID);
            //remove trade and create offer inplace of trade
            if (obj.Type != -1)
            {
                TradeObj obj2 = TradeObjByID(obj.Type);
                if (obj2 == null)
                {
                    player.SendErrorMessage("Error: Major Database Desync has occured - Transaction ID: {0} does not exist!", obj.Type);
                    return;
                }
                main.Database.Query("DELETE FROM storetrade WHERE ID = @0", obj2.ID);
                main.Database.Query("INSERT INTO storeoffer(ID, User, ItemID, Stack, TradeID) VALUES(@0,@1,@2,@3,@4)", offerID, player.Name, obj2.ItemID, obj2.Stack, -1);
                offerID += 1;
                tradeObj.Remove(obj2);
            }
            offerObj.Remove(obj);
        }

        public void returnOffer(OfferObj obj)
        {
            main.Database.Query("UPDATE storeoffer SET TradeID = @0 WHERE ID = @1", -1, obj.ID);
        }

        public void updateTradeData()
        {
            QueryResult result = main.Database.QueryReader("SELECT * FROM storetrade");
            using (result)
            {
                while (result.Read())
                {
                    this.tradeObj.Add(new TradeObj(result.Get<int>("ID"), result.Get<string>("User"), result.Get<int>("ItemID"), result.Get<int>("Stack"), result.Get<int>("WItemID"), result.Get<int>("WStack")));
                    if (tradeID <= result.Get<int>("ID"))
                    {
                        tradeID = result.Get<int>("ID") + 1;
                    }
                }
            }
            result.Dispose();
        }
        public void updateOfferData()
        {
            QueryResult result = main.Database.QueryReader("SELECT * FROM storeoffer");
            using (result)
            {
                while (result.Read())
                {
                    this.offerObj.Add(new OfferObj(result.Get<int>("ID"), result.Get<string>("User"), result.Get<int>("ItemID"), result.Get<int>("Stack"), result.Get<int>("TradeID")));
                    if (offerID <= result.Get<int>("ID"))
                    {
                        offerID = result.Get<int>("ID") + 1;
                    }
                }
            }
            result.Dispose();
        }
        public TradeData(Shop instance)
        {
            main = instance;
            updateTradeData();
            updateOfferData();
        }
    }
}
