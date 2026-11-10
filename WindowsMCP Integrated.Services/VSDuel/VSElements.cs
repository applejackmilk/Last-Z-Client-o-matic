using System;
using System.Collections.Generic;
using System.Drawing;
using System.Text;
using System.Xml.Linq;
using WindowsMCP_Integrated.Core;
using WindowsMCP_Integrated.Core.UI;

namespace WindowsMCP_Integrated.VSDuel
{
    public class VSElements
    {
        private readonly RankingsWindow rankings;
        protected static ElementLocation RankingButton = ElementLocation.CreateFromTopLeft("Ranking Button", 65, 1127, 55, 43, ContainerName.VSDuel);
        protected static ElementLocation firstChestElement = ElementLocation.Create(name: "VS First Check", centerX: 822, centerY: 449, width: 25, height: 25, container: ContainerName.Unknown);
        protected static ElementLocation chestsNextElement = ElementLocation.Create(name: "VS Chests Next Page", centerX: 1181, centerY: 461, width: 25, height: 25, container: ContainerName.Unknown);
        protected static ElementLocation chestsPreviousElement = ElementLocation.Create(name: "VS Chests Previous Page", centerX: 738, centerY: 462, width: 25, height: 25, container: ContainerName.Unknown);
        protected static ElementLocation vsBackButtonElement = ElementLocation.Create(name: "VS Page Back", centerX: 761, centerY: 984, width: 25, height: 25, container: ContainerName.Unknown);

        internal RankingsWindow Rankings => rankings;
        internal ElementLocation RankingsButton => RankingButton;
        internal ElementLocation FirstChestElement => firstChestElement;
        internal ElementLocation ChestsNextElement => chestsNextElement;
        internal ElementLocation ChestsPreviousElement => chestsPreviousElement;
        internal ElementLocation BackButtonElement => vsBackButtonElement;

        public VSElements()
        {
            rankings = new RankingsWindow();
        }

        internal class RankingsWindow
        {
            private static ElementLocation allianceCheckbox = ElementLocation.CreateFromTopLeft(name: "My Alliance", top: 807, left: 888, width: 27, height: 25, container: ContainerName.VSDuel);
            private static ScrollableReadableTable DailyRankingTable = ScrollableReadableTable.Create(name: "Daily Ranking Table"
                , readRegionX: 750, readRegionY: 315
                , readRegionWidth: 420, readRegionHeight: 365
                , cursorStartX: 1149, cursorStartY: 663
                , estimatedPartitions: 3);
            private static ElementLocation exitButtonElement = ElementLocation.Create(name: "Daily Ranking Exit Button", centerX: 1148, centerY: 178, width: 25, height: 25, container: ContainerName.Unknown);

            public ScrollableReadableTable DailyRankingsTable => DailyRankingTable;
            private DailyRankingTabs _dailyRankingTabs = new DailyRankingTabs();
            public ElementLocation AllianceCheckbox => allianceCheckbox;
            public ElementLocation ExitButton => exitButtonElement;
            public ElementLocation GetDayElement(int index)
            {
                return _dailyRankingTabs.GetDayElement(index);
            }

            public ElementLocation GetTabElement(int index)
            {
                var rankingTabs = new RankingTabs();
                return rankingTabs.GetTabElement(index);
            }

            protected class RankingTabs : UIContainerAbstract
            {
                public RankingTabs() : base(x: 754, y: 207, width: 410, height: 48, ContainerName.VSDuel)
                {
                    _defaultChildSize = new Size(width: 139, height: 41);
                    AddChild("Daily Rank");
                    AddChild("This Week's Ranking");
                    AddChild("Overall Rank");
                }

                public ElementLocation GetTabElement(int index)
                    => GetElementByIndex(index);
            }
            protected class DailyRankingTabs : UIContainerAbstract
            {
                public ElementLocation GetDayElement(int dayIndex)
                    => GetElementByIndex(dayIndex);

                public DailyRankingTabs() : base(x: 755, y: 270, width: 411, height: 46, ContainerName.VSDuel)
                {
                    _defaultChildSize = new Size(width: 68, height: 38);
                    for (int i = 0; i < 6; i++)
                    {
                        AddChild(String.Join("", ((System.DayOfWeek)(i + 1)).ToString().Take(3)));
                    }
                }
            }
        }

    }
}
