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
            main.Database.Query("INSERT INTO storetrade(ID, User, ItemID, Stack, WItemID, WStack, Active) VALUES(@0,@1,@2,@3,@4,@5,@6)", tradeID, user, itemid, stack, witemid, wstack, 1);
            this.tradeObj.Add(new TradeObj(tradeID, user, itemid, stack, witemid, wstack, 1));
            tradeID += 1;
        }

        internal void addExchange(TSPlayer player, Item item, int stack, int money)
        {
            main.Database.Query("INSERT INTO storetrade(ID, User, ItemID, Stack, WItemID, WStack, Active) VALUES(@0,@1,@2,@3,@4,@5)", tradeID, player.Name, item.netID, stack, 0, money, 1);
            this.tradeObj.Add(new TradeObj(tradeID, player.Name, item.netID, stack, 0, money, 1));
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
            //make trade inactive as it has been finished -in db and in cached obj
            main.Database.Query("UPDATE storetrade SET Active = @0 WHERE ID = @1", 0, obj.ID);
            obj.Active = 0;
            //check if item being added to db/obj is actually an item
            if (item != null)
            {
                //add to db/obj as a comppleted offer, so player who added the trade is able to collect it
                main.Database.Query("INSERT INTO storeoffer(ID, User, ItemID, Stack, TradeID, Active) VALUES(@0,@1,@2,@3,@4,@5)", offerID, obj.User, item.netID, stack, -1, 1);
                offerObj.Add(new OfferObj(offerID, obj.User, item.netID, stack, -1, 1));
            }
            else
            {
                //add to db/obj as completed offer but for currency instead, so player who added the trade isable to collect it
                main.Database.Query("INSERT INTO storeoffer(ID, User, ItemID, Stack, TradeID, Active) VALUES(@0,@1,@2,@3,@4,@5)", offerID, obj.User, 0, stack, -1, 1);
                offerObj.Add(new OfferObj(offerID, obj.User, 0, stack, -1, 1));
            }
            //increase offer ID
            offerID += 1;            
        }
        //adds a trade offer
        public void processOffer(TSPlayer player, int id, int itemid, int stack)
        {
            main.Database.Query("INSERT INTO storeoffer(ID, User, ItemID, Stack, TradeID, Active) VALUES(@0,@1,@2,@3,@4,@5)", offerID, player.Name, itemid, stack, id, 1);
            offerObj.Add(new OfferObj(offerID, player.Name, itemid, stack, id, 1));
            offerID += 1;
        }

        internal void processAccept(TSPlayer player, OfferObj obj)
        {
            //deactivate offer in db
            main.Database.Query("UPDATE storeoffer SET Active = @0 WHERE ID = @1", 0, obj.ID);
            //remove trade and create offer inplace of trade
            if (obj.Type != -1)
            {
                TradeObj obj2 = TradeObjByID(obj.Type);
                if (obj2 == null)
                {
                    player.SendErrorMessage("Error: Major Database Desync has occured - Transaction ID: {0} does not exist!", obj.Type);
                    return;
                }
                main.Database.Query("UPDATE storetrade SET TradeID = @0 WHERE ID = @1 AND Active = @2",0 ,obj2.ID, 1);
                main.Database.Query("INSERT INTO storeoffer(ID, User, ItemID, Stack, TradeID, Active) VALUES(@0,@1,@2,@3,@4,@5)", offerID, obj.User, obj2.ItemID, obj2.Stack, -1, 1);
                offerObj.Add(new OfferObj(offerID, obj.User, obj2.ItemID, obj2.Stack, -1, 1));
                offerID += 1;
                obj2.Active = 0;
            }
            obj.Active = 0;
        }

        public void returnOffer(OfferObj obj)
        {
            main.Database.Query("UPDATE storeoffer SET TradeID = @0 WHERE ID = @1 AND Active = @2", -1, obj.ID, 1);
            obj.Type = -1;
        }

        internal void cancelTrade(TradeObj obj)
        {
            main.Database.Query("UPDATE storetrade SET Active = @0 WHERE ID = @1", 0, obj.ID);
            main.Database.Query("INSERT INTO storeoffer(ID, User, ItemID, Stack, TradeID, Active) VALUES(@0,@1,@2,@3,@4,@5)", offerID, obj.User, obj.ItemID, obj.Stack, -1, 1);
            offerObj.Add(new OfferObj(offerID, obj.User, obj.ItemID, obj.Stack, -1, 1));
            offerID += 1;
            foreach (OfferObj obj2 in offerObj)
            {
                if (obj2.Type == obj.ID && obj.Active == 1)
                {
                    main.Database.Query("UPDATE storeoffer SET TradeID = @0 WHERE ID = @1", -1, obj.ID);
                    obj2.Type = -1;
                }
            }
            obj.Active = 0;
        }

        internal void cancelOffers(string name)
        {
            foreach (OfferObj obj in offerObj)
            {
                if (obj.User == name && obj.Active == 1)
                {
                    main.Database.Query("UPDATE storeoffer SET TradeID = @0 WHERE ID = @1", -1, obj.ID);
                    obj.Type = -1;
                }
            }
        }

        public void updateTradeData()
        {
            QueryResult result = main.Database.QueryReader("SELECT * FROM storetrade");
            using (result)
            {
                while (result.Read())
                {
                    this.tradeObj.Add(new TradeObj(result.Get<int>("ID"), result.Get<string>("User"), result.Get<int>("ItemID"), result.Get<int>("Stack"), result.Get<int>("WItemID"), result.Get<int>("WStack"), result.Get<int>("Active")));
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
                    this.offerObj.Add(new OfferObj(result.Get<int>("ID"), result.Get<string>("User"), result.Get<int>("ItemID"), result.Get<int>("Stack"), result.Get<int>("TradeID"), result.Get<int>("Active")));
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
