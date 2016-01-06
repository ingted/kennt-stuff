// This namespace holds all strategies and is required. Do not change it.

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Linq;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using KenNinja;
using NinjaTrader.Cbi;

namespace NinjaTrader.Custom.Strategy
{
    /// <summary>
    /// Get Some Data
    /// </summary>
    [Description("Get Some Data")]
    public class StocCross : NinjaTrader.Strategy.Strategy
    {
        // User defined variables (add any user defined variables below)

        private static readonly List<Kp> KpsToUse;
        private static int tradeId = 0;
        protected IDbConnection DbConn;
        private int _bears;
        private int _bulls;
        private int _winningBears;
        private int _winningBulls;
       

        private SortedList<Guid, ActiveOrder> activerOrders;
        private int myInput0 = 1; // Default setting for MyInput0
        private double _strikeWidth;


        //Configure the allowed patterns that are significant order by performance.
        static StocCross()
        {
            var list = new[]
            {
                109,
                111, 103,
                105,
                -107,
                -202,
                -102,
                102,
                106,
                -112,
                -104,
                -103,
                104,
                112
            };

            list = Enum.GetValues(typeof(Kp)).Cast<Kp>().Select(z => z.ToInt()).ToArray();




            var validValues = Enum.GetValues(typeof(Kp)).Cast<Kp>().Select(z => z.ToInt());
            KpsToUse = list.Where(validValues.Contains).Cast<Kp>().ToList();
        }


        protected override void Initialize()
        {

            var _stikeWidths = new SortedList<string, double> { { "$EURUSD", .01 }, { "$GPDUSD", .015 }, { "$USDJPY", 1.0 }, { "$AUDUSD", .01 }, { "$USDCAD", .01 }, { "$EURJPY", 1.0 } };
            var instrument = this.Instrument.ToString().ToUpper().Replace("DEFAULT", "").Replace(" ", "");
            Print(instrument);
            if (_stikeWidths.ContainsKey(instrument))
            {
                _strikeWidth = _stikeWidths[instrument];

            }
            else
            {
                Print("Fail");
            }





            Log(string.Format("Starting for StocScorr {0}", Instrument), LogLevel.Information);
            ClearOutputWindow();
            CalculateOnBarClose = true; // only on bar close( this is a candle stick strategy)
            activerOrders = new SortedList<Guid, ActiveOrder>();
            _bulls = 0;
            _winningBulls = 0;
            _bears = 0;
            _winningBears = 0;

        





        }


        protected override void OnBarUpdate()
        {

            if (!Historical)
                Log(string.Format("OnBarUpdate for {0}", Instrument), LogLevel.Information);

            HandleCurrentOrders();



            Print(string.Format("{0} of {1} bulls successful", _winningBulls, _bulls));
            Print(string.Format("{0} of {1} bears successful", _winningBears, _bears));
            Print(string.Format("{0} of {1} all successful", _winningBears + _winningBulls, _bears + _bulls));



            double candlestick = 0;

            var barTime = DateTime.Parse(Time.ToString());





            //Is there any sentiment found

            var isBull = CrossAbove(this.Stochastics(7, 14, 3).K, this.Stochastics(7, 14, 3).D, 10) &&
                         this.Stochastics(7, 14, 3).D[0] < 20;
            var isBear = false;
        
          
                if (isBull || isBear)
                {
                    var expiryTime = barTime.AddHours(1);

                    var
                        order = new ActiveOrder
                        {
                            Id = Guid.NewGuid(),
                            Time = barTime,
                            ExpiryHour = expiryTime.Hour,
                            ExpiryDay = expiryTime.Day,
                            EnteredAt = Close[0],
                        };


                     
                    if (isBull)
                    {
                        order.IsLong = true;
                        order.ExitAt = Close[0] + (Math.Abs(_strikeWidth) / 2);
                        activerOrders.Add(order.Id, order);
                        _bulls++;

                        SendNotification(candlestick);
                    }
                    

                    /*

                    if (IsBearishSentiment(candlestick))
                    {
                        order.IsLong = false;
                        order.ExitAt = Close[0] - (Math.Abs(_strikeWidth) / 2);

                        activerOrders.Add(order.Id, order);
                        _bears++;


                        SendNotification(candlestick);
                    }
                     * */
                }
            



        }

        private void SendNotification(double candlestick)
        {

            if (Historical)
                return;


            var isBull = IsBullishSentiment(candlestick);
            var mailSubject = string.Format("KC-SIGNAL-{0}: {1} on {2} @ {3}", (isBull) ? "BULL" : "BEAR",
                (Kp)candlestick, Instrument, Close[0]);
            var mailContentTemplate = @"A {0} {4} signal was observed in '{1}' at {2} at a closing price of {3}.";
            var mailContent = string.Format(mailContentTemplate, (isBull) ? "BULL" : "BEAR", Instrument, Time, Close[0],
                (Kp)candlestick);
            SendMail("hoskinsken@gmail.com", "hoskinsken@gmail.com", mailSubject, mailContent);
        }

        private void HandleCurrentOrders()
        {
            if (activerOrders.Any())
            {
                var currentNow = DateTime.Parse(Time.ToString());

                var successfulBulls = activerOrders.Values.Where(z => z.IsLong && High[0] >= z.ExitAt).ToList();
                foreach (var success in successfulBulls)
                {

                    activerOrders.Remove(success.Id);
                    _winningBulls++;
                }


                var successfulBears = activerOrders.Values.Where(z => !z.IsLong && Low[0] <= z.ExitAt).ToList();
                foreach (var success in successfulBears)
                {
                    activerOrders.Remove(success.Id);
                    _winningBears++;
                }

                if (currentNow.Minute == 00)
                {
                    var closingOrders =
                        activerOrders.Values.Where(z => z.ExpiryDay == currentNow.Day && z.ExpiryHour == currentNow.Hour)
                            .ToList();

                    foreach (var candidate in closingOrders)
                    {
                        if (candidate.IsLong && candidate.EnteredAt < Close[0])
                        {
                            _winningBulls++;
                        }
                        if (!candidate.IsLong && candidate.EnteredAt > Close[0])
                        {
                            _winningBears++;
                        }
                        activerOrders.Remove(candidate.Id);
                    }
                }
            }
        }


        private static bool IsBearishSentiment(double candleStick)
        {
            return candleStick < -99;
        }

        private static bool IsBullishSentiment(double candleStick)
        {
            return candleStick > 99;
        }


        private class ActiveOrder
        {
            public Guid Id { get; set; }
            public DateTime Time { get; set; }
            public int ExpiryHour { get; set; }
            public int ExpiryDay { get; set; }
            public bool IsLong { get; set; }
            public double EnteredAt { get; set; }
            public double ExitAt { get; set; }
         
        }
    }
}