using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Data;
using System.IO;
using System.Text;

using Mono.Data.Sqlite;
using MySql.Data.MySqlClient;

using TerrariaApi.Server;
using Terraria;

using TShockAPI;
using TShockAPI.DB;
using TShockAPI.Hooks;

using System.Reflection;
using System.Web;

using Wolfje.Plugins.SEconomy;
using Wolfje.Plugins.SEconomy.Economy;
using Wolfje.Plugins.SEconomy.Journal;
using shop;


namespace Shop
{
    [ApiVersion(1, 14)]
    public class Shop : TerrariaPlugin
    {
        internal ShopData ShopList;
        public IDbConnection Database;
        public String SavePath = TShock.SavePath;
        public shopConfig configObj { get; set; }
        internal static string filepath { get { return Path.Combine(TShock.SavePath, "alternativeroot.json"); } }

        public override string Author
        {
            get { return "IcyPhoenix"; }
        }
        public override string Description
        {
            get { return "SEconomy based Shop"; }
        }

        public override string Name
        {
            get { return "Shop"; }
        }

        public override Version Version
        {
            get { return new Version("1.0"); }
        }
        public Shop(Main game)
            : base(game)
        {
            Order = 10;
        }
        public override void Initialize()
        {
            configObj = new shop.shopConfig();
            SetupConfig();
            ShopList = new ShopData(this);

            switch (TShock.Config.StorageType.ToLower())
            {
                case "mysql":
                    string[] host = TShock.Config.MySqlHost.Split(':');
                    Database = new MySqlConnection()
                    {
                        ConnectionString = string.Format("Server={0}; Port={1}; Database={2}; Uid={3}; Pwd={4};",
                        host[0],
                        host.Length == 1 ? "3306" : host[1],
                        TShock.Config.MySqlDbName,
                        TShock.Config.MySqlUsername,
                        TShock.Config.MySqlPassword)

                    };
                    break;
                case "sqlite":

                    // Create folder if it does not exist. (SQLITE Specific.)
                    if (!System.IO.Directory.Exists(SavePath))
                    {
                        System.IO.Directory.CreateDirectory(SavePath);
                    }


                    string sql = Path.Combine(SavePath, "alternativeroot.sqlite");
                    Database = new SqliteConnection(string.Format("uri=file://{0},Version=3", sql));
                    break;
            }
            SqlTableCreator sqlcreator = new SqlTableCreator(Database, Database.GetSqlType() == SqlType.Sqlite ? (IQueryBuilder)new SqliteQueryCreator() : new MysqlQueryCreator());
            sqlcreator.EnsureExists(new SqlTable("alternativeroot",
                new SqlColumn("name", MySqlDbType.VarChar) { Primary = true, Length = 30 },
                new SqlColumn("price", MySqlDbType.Int32),
                new SqlColumn("region", MySqlDbType.VarChar) { Length = 30 },
                new SqlColumn("group", MySqlDbType.VarChar) { Length = 30 },
                new SqlColumn("restockTimer", MySqlDbType.Int32),
                new SqlColumn("stock", MySqlDbType.Int32),
                new SqlColumn("onsale", MySqlDbType.VarChar) { Length = 30 }
                ));

            Commands.ChatCommands.Add(new Command("", shop, "shop"));
            Commands.ChatCommands.Add(new Command("shop.reloadItems", shopreload, "shopreload"));
        }

        private void shopreload(CommandArgs args)
        {
            throw new NotImplementedException();
        }

        private void shop(CommandArgs args)
        {
            //main args switch
            string Switch = args.Parameters[0].ToLower();
            if (Switch == "buy")
            {
                if (!args.Player.Group.HasPermission("shop.buyItems"))
                {
                    args.Player.SendErrorMessage("Error: You do not have permission to buy items!");
                    return;
                }
                //display help as no item specificed | or too many params
                if (args.Parameters.Count == 1 || args.Parameters.Count >= 4)
                {
                    args.Player.SendInfoMessage("Info: /shop buy Item [Amount]");
                    return;
                }
                //purchase without stack specified
                if (args.Parameters.Count == 2)
                {
                    BuyItem(args.Player, args.Parameters[1]);
                }
                //purchase with stack specified
                if (args.Parameters.Count == 3)
                {
                    BuyItem(args.Player, args.Parameters[1], Convert.ToInt32(args.Parameters[2]));
                }
            }
        }

        void BuyItem(TSPlayer player, string itemNameOrId, int stack = 1)
        {
            //Check if you can purchase that many stacks of that item in the first place...
            Item item = getItem(player, itemNameOrId, stack);
            //if null then exit
            if (item == null)
            {
                return;
            }

            //find item value
            int index = ShopList.name.IndexOf(item.name);            
            int cost = ShopList.price[index];

            //check if onsale if yes lower cost amount
            if (ShopList.onsale[index] != "")
            {
                foreach (string str in ShopList.onsale[index].Split(new char[] { ',' }))
                {
                    switch (str.ToLower())
                    {
                        case "bloodmoon":
                            break;
                        case "eclipse":
                            break;
                        case "night":
                            break;
                        case "day":
                            break;
                    }
                }
            }

            //Check if in stock
            if (ShopList.stock[index] == 0)
            {
                player.SendErrorMessage("Error: No current stock for {0}", item.name);
                return;
            }

            //Check if has locked down group permissions
            if (!groupAllowed(player, index))
            {
                player.SendErrorMessage("Error: You do not have permissions to purchases {0}", item.name);
                return;
            }

            //Check if player has enough money to purchase the item
            var account = Wolfje.Plugins.SEconomy.SEconomyPlugin.GetEconomyPlayerSafe(player.Index);
            //Make sure balance of user is greater then group cost
            if (account.BankAccount.Balance < cost)
            {
                player.SendErrorMessage("Error: You need {1} {0} to purchase item(s)", Wolfje.Plugins.SEconomy.SEconomyPlugin.Configuration.MoneyConfiguration.MoneyName, cost);
                return;
            }

            if (!inRegion(player, index))
            {
                player.SendErrorMessage("Error: You are currently not in range of the shop");
                return;
            }

            //Check if player has free inventory
            if (freeSlots(player, item, stack))
            {
                //All checks completed
                //Remove money and place in worldaccount
                Wolfje.Plugins.SEconomy.SEconomyPlugin.WorldAccount.TransferToAsync(account.BankAccount, -cost, Wolfje.Plugins.SEconomy.Journal.BankAccountTransferOptions.IsPayment, string.Format("Successfully Paid: {0}", cost), string.Format("Shop: {0} purhcase {1} stack of {2}", player.Name, stack, item.name));
            }
            if (ShopList.stock[index] != -1)
            {
                ShopList.stock[index] -= 1;
            }
        }

        private Boolean freeSlots(TSPlayer player, Item item, int itemAmount)
        {
            if (player.InventorySlotAvailable || (item.name.Contains("Coin") && item.type != 905) || item.type == 58 || item.type == 184)
            {
                if (player.GiveItemCheck(item.type, item.name, item.width, item.height, itemAmount))
                {
                    return true;
                }
                return false;
            }
            else
            {
                player.SendErrorMessage("Your inventory seems full.");
                return false;
            }
        }

        private Boolean inRegion(TSPlayer player, int index)
        {
            if (ShopList.region[index] != null)
            {
                foreach (Region region in ShopList.region[index])
                {
                    if (region.InArea(new Rectangle((int)(player.TileX), (int)(player.TileY), player.TPlayer.width, player.TPlayer.height)))
                    {
                        return true;
                    }
                }
                return false;
            }
            return true;
        }

        private Boolean groupAllowed(TSPlayer player, int index)
        {
            if (ShopList.group[index] != null)
            {
                var cur = player.Group;
                var traversed = new List<Group>();
                var allowedGroups = ShopList.group[index];
                while (cur != null)
                {
                    if (allowedGroups.Contains(cur))
                    {
                        return true;
                    }
                    if (traversed.Contains(cur))
                    {
                        throw new InvalidOperationException("Infinite group parenting ({0})".SFormat(cur.Name));
                    }
                    traversed.Add(cur);
                    cur = cur.Parent;
                }
                return false;
            }
            return true;
        }

        private Item getItem(TSPlayer player, string itemNameOrId, int stack)
        {
            Item item = new Item();
            List<Item> matchedItems = TShock.Utils.GetItemByIdOrName(itemNameOrId);
            if (matchedItems.Count == 0)
            {
                player.SendErrorMessage("Error: Invalid item type!");
                return null;
            }
            else if (matchedItems.Count > 1)
            {
                TShock.Utils.SendMultipleMatchError(player, matchedItems.Select(i => i.name));
                return null;
            }
            else
            {
                item = matchedItems[0];
            }
            if (stack > item.maxStack)
            {
                player.SendErrorMessage("Error: Purchase stacks is greater then maximum stack size");
                return null;
            }
            //all checks passed return true;
            return item;
        }

        private void SetupConfig()
        {
            try
            {
                if (File.Exists(filepath))
                {
                    configObj = new shopConfig();
                    configObj = shopConfig.Read(filepath);
                    return;
                }
                else
                {
                    Log.ConsoleError("Shop config not found. Creating new one");
                    configObj.Write(filepath);
                    return;
                }
            }
            catch (Exception ex)
            {
                Log.ConsoleError(ex.Message);
                return;
            }
        }
    }
}