using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Shop
{
    internal class TradeObj
    {
        public int ID = new int();
        public string User = "";
        public int ItemID = new int();
        public int Stack = new int();
        public int WItemID = new int();
        public int WStack = new int();
        public int Active = new int();

        public TradeObj(int id, string user, int itemid, int stack, int witemid, int wstack, int active)
        {
            this.ID = id;
            this.User = user;
            this.ItemID = itemid;
            this.Stack = stack;
            this.WItemID = witemid;
            this.WStack = wstack;
            this.Active = active;
        }
    }
}
