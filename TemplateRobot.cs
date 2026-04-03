using OsEngine.Entity;
using OsEngine.Indicators;
using OsEngine.Logging;
using OsEngine.OsTrader.Panels;
using OsEngine.OsTrader.Panels.Attributes;
using OsEngine.OsTrader.Panels.Tab;
using System;
using System.Collections.Generic;

/* Description
trading robot for osengine

TODO: описание стратегии

Buy:
TODO

Sell:
TODO

Exit from buy:
TODO

Exit from sell:
TODO

Volume:
Calculated as % of deposit risk per trade, accounting for the distance to the stop,
slippage and commission. Supports SPOT / LinearPerpetual, Stocks MOEX, InversFutures, Futures MOEX.
*/

namespace OsEngine.Robots
{
    [Bot("TemplateRobot")]
    public class TemplateRobot : BotPanel
    {
        // ── Вкладка ──────────────────────────────────────────────────────────────────
        private readonly BotTabSimple _tab;

        // ── Базовые параметры ────────────────────────────────────────────────────────
        private readonly StrategyParameterString _regime;

        // ── Параметры объёма ─────────────────────────────────────────────────────────
        private readonly StrategyParameterString _modeTrade;
        private readonly StrategyParameterString _assetNameCurrent;
        private readonly StrategyParameterDecimal _volumeLong;
        private readonly StrategyParameterDecimal _volumeShort;
        private readonly StrategyParameterDecimal _slippagePercent;
        private readonly StrategyParameterDecimal _feePercent;
        private readonly StrategyParameterButton _tradePeriodsShowDialogButton;

        // ── Параметры неторгового времени ────────────────────────────────────────────
        private readonly StrategyParameterInt _timeZoneUtc;
        private bool _nonTradePeriodLogged;
        private readonly NonTradePeriods _tradePeriods;

        // ── TODO: добавить параметры индикаторов здесь ───────────────────────────────
        // private readonly StrategyParameterInt _lengthMyIndicator;

        // ── TODO: добавить индикаторы здесь ──────────────────────────────────────────
        // private Aindicator _myIndicator;

        // ── Рабочие переменные расчёта объёма ────────────────────────────────────────
        // Синхронизированные копии параметров GUI (обновляются через SyncParams)
        private decimal _curVolumeLong;
        private decimal _curVolumeShort;
        private decimal _curSlippagePercent;
        private decimal _curFeePercent;
        private int _curTimeZoneUtc;
        // Временные поля, используются только внутри одного синхронного вызова GetVolume()
        private decimal _curStopPercent;   // % расстояния до стопа от цены входа
        private decimal _curStopPrice;     // абсолютная цена стопа (нужна для Futures MOEX)

        // ── Словарь стопов ───────────────────────────────────────────────────────────
        //
        //   Ключ     = order.NumberUser — уникальный int, присваивается движком при создании
        //              ордера ВНУТРИ BuyAtLimit/SellAtLimit, до отправки на биржу.
        //   Значение = начальная цена стопа, рассчитанная для этой конкретной заявки.
        //
        //   Запись добавляется сразу после BuyAtLimit (синхронно).
        //   Запись удаляется в SetStopLoss после выставления начального стопа (асинхронно).
        //   При нескольких одновременных позициях каждая хранит свой стоп под своим ключом.
        private readonly Dictionary<int, decimal> _stopByOrderId = new Dictionary<int, decimal>();

        // ════════════════════════════════════════════════════════════════════════════
        //   КОНСТРУКТОР
        // ════════════════════════════════════════════════════════════════════════════

        public TemplateRobot(string name, StartProgram startProgram) : base(name, startProgram)
        {
            _tradePeriods = new NonTradePeriods(name);
            _tradePeriods.NonTradePeriodGeneral.NonTradePeriod1Start = new TimeOfDay { Hour = 0, Minute = 0 };
            _tradePeriods.NonTradePeriodGeneral.NonTradePeriod1End = new TimeOfDay { Hour = 10, Minute = 5 };
            _tradePeriods.NonTradePeriodGeneral.NonTradePeriod1OnOff = true;
            _tradePeriods.NonTradePeriodGeneral.NonTradePeriod2Start = new TimeOfDay { Hour = 13, Minute = 54 };
            _tradePeriods.NonTradePeriodGeneral.NonTradePeriod2End = new TimeOfDay { Hour = 14, Minute = 6 };
            _tradePeriods.NonTradePeriodGeneral.NonTradePeriod2OnOff = false;
            _tradePeriods.NonTradePeriodGeneral.NonTradePeriod3Start = new TimeOfDay { Hour = 18, Minute = 1 };
            _tradePeriods.NonTradePeriodGeneral.NonTradePeriod3End = new TimeOfDay { Hour = 23, Minute = 58 };
            _tradePeriods.NonTradePeriodGeneral.NonTradePeriod3OnOff = true;
            _tradePeriods.TradeInSunday = false;
            _tradePeriods.TradeInSaturday = false;
            _tradePeriods.Load();

            TabCreate(BotTabType.Simple);
            _tab = TabsSimple[0];

            _tradePeriodsShowDialogButton = CreateParameterButton("Non trade periods", "Base");
            _tradePeriodsShowDialogButton.UserClickOnButtonEvent += () => _tradePeriods.ShowDialog();

            // Базовые настройки
            _regime = CreateParameter("Regime", "Off",
                new[] { "Off", "On", "LONG-POS", "SHORT-POS", "CLOSE-POS" }, "Base");
            _timeZoneUtc = CreateParameter("Time zone UTC", 4, -24, 24, 1, "Base");

            // Настройки объёма
            _modeTrade = CreateParameter("Trade Section",
                "SPOT и LinearPerpetual",
                new[] { "SPOT и LinearPerpetual", "InversFutures", "Stocks MOEX", "Futures MOEX" }, "Base");
            _assetNameCurrent = CreateParameter("Deposit Asset", "USDT",
                new[] { "USDT", "USDC", "USD", "RUB", "EUR", "BTC", "Prime" }, "Base");
            _volumeLong = CreateParameter("Volume Long (%)", 2.5m, 0.1m, 50m, 0.1m, "Base");
            _volumeShort = CreateParameter("Volume Short (%)", 2.5m, 0.1m, 50m, 0.1m, "Base");
            _slippagePercent = CreateParameter("Slippage (%)", 0.1m, 0.01m, 2m, 0.01m, "Base");
            _feePercent = CreateParameter("Fee (%)", 0.1m, 0.01m, 1m, 0.01m, "Base");

            // TODO: создать параметры индикаторов здесь
            // _lengthMyIndicator = CreateParameter("Length", 20, 5, 200, 5, "Indicator");

            // TODO: создать и подключить индикаторы здесь
            // _myIndicator = IndicatorsFactory.CreateIndicatorByName("MyIndicator", name + "MyIndicator", false);
            // _myIndicator = (Aindicator)_tab.CreateCandleIndicator(_myIndicator, "Prime");
            // ((IndicatorParameterInt)_myIndicator.Parameters[0]).ValueInt = _lengthMyIndicator.ValueInt;
            // _myIndicator.Save();

            // Подписки
            ParametrsChangeByUser += OnParametrsChangeByUser;
            _tab.CandleFinishedEvent += OnCandleFinished;
            _tab.PositionOpeningSuccesEvent += SetStopLoss;  // асинхронно, после подтверждения биржи

            SyncParams();

            Description = "TODO: описание робота. " +
                          "Volume is calculated as % of deposit risk per trade.";
        }

        // ════════════════════════════════════════════════════════════════════════════
        //   ОБЯЗАТЕЛЬНЫЕ ПЕРЕГРУЗКИ
        // ════════════════════════════════════════════════════════════════════════════

        public override string GetNameStrategyType() => "TemplateRobot";

        public override void ShowIndividualSettingsDialog() { }

        // ════════════════════════════════════════════════════════════════════════════
        //   СИНХРОНИЗАЦИЯ ПАРАМЕТРОВ
        // ════════════════════════════════════════════════════════════════════════════

        private void OnParametrsChangeByUser()
        {
            // TODO: обновить параметры индикаторов здесь
            // ((IndicatorParameterInt)_myIndicator.Parameters[0]).ValueInt = _lengthMyIndicator.ValueInt;
            // _myIndicator.Save();
            // _myIndicator.Reload();

            SyncParams();
        }

        private DateTime LocalTime(DateTime utcTime)
        {
            if (utcTime == DateTime.MinValue) return utcTime;
            return utcTime.AddHours(_curTimeZoneUtc);
        }

        private void SyncParams()
        {
            _curVolumeLong = _volumeLong.ValueDecimal;
            _curVolumeShort = _volumeShort.ValueDecimal;
            _curSlippagePercent = _slippagePercent.ValueDecimal;
            _curFeePercent = _feePercent.ValueDecimal;
            _curTimeZoneUtc = _timeZoneUtc.ValueInt;
        }

        // ════════════════════════════════════════════════════════════════════════════
        //   ГЛАВНЫЙ ОБРАБОТЧИК СВЕЧИ
        // ════════════════════════════════════════════════════════════════════════════

        private void OnCandleFinished(List<Candle> candles)
        {
            if (_regime.ValueString == "Off") return;

            // TODO: заменить минимальное количество свечей под свой индикатор
            if (candles.Count < 20) return;

            DateTime now = LocalTime(_tab.TimeServerCurrent);
            if (!_tradePeriods.CanTradeThisTime(now))
            {
                if (!_nonTradePeriodLogged)
                {
                    _nonTradePeriodLogged = true;
                    SendNewLogMessage($"⏰ Неторговое время (UTC+{_curTimeZoneUtc}): {now:HH:mm:ss} — входы заблокированы", LogMessageType.System);
                }
                return;
            }

            if (_nonTradePeriodLogged)
            {
                _nonTradePeriodLogged = false;
                SendNewLogMessage($"✅ Торговое время возобновлено (UTC+{_curTimeZoneUtc}): {now:HH:mm:ss}", LogMessageType.System);
            }

            List<Position> openPositions = _tab.PositionsOpenAll;

            if (openPositions != null && openPositions.Count != 0)
                LogicClosePosition(candles);

            if (_regime.ValueString == "CLOSE-POS") return;

            if (openPositions == null || openPositions.Count == 0)
                LogicOpenPosition(candles);
        }

        // ════════════════════════════════════════════════════════════════════════════
        //   ЛОГИКА ОТКРЫТИЯ — TODO: реализовать
        //
        //   Шаблон показывает правильный порядок действий:
        //   1. Получить сигнальные значения индикаторов
        //   2. Определить цену стопа
        //   3. Рассчитать объём через CalcVolume(side, entry, stopPrice)
        //   4. Отправить ордер и сохранить стоп в словарь _stopByOrderId
        // ════════════════════════════════════════════════════════════════════════════

        private void LogicOpenPosition(List<Candle> candles)
        {
            decimal lastPrice = candles[candles.Count - 1].Close;

            // TODO: получить значения индикатора
            // decimal signalValue = _myIndicator.DataSeries[0].Last;

            // ── LONG ─────────────────────────────────────────────────────────────────
            if (_regime.ValueString == "LONG-POS")
            {
                // TODO: условие входа в лонг
                // if (lastPrice > signalValue)
                // {
                //     decimal entry = _tab.PriceBestAsk;
                //     if (entry <= 0) entry = lastPrice;
                //
                //     decimal stopPrice = /* TODO: ваш уровень стопа */;
                //     if (stopPrice <= 0 || stopPrice >= entry) return;
                //
                //     decimal volume = CalcVolume(Side.Buy, entry, stopPrice);
                //     if (volume <= 0) return;
                //
                //     decimal slippage = entry * (_curSlippagePercent / 100m);
                //     Position pos = _tab.BuyAtLimit(volume, entry + slippage);
                //
                //     if (pos == null || pos.OpenOrders == null || pos.OpenOrders.Count == 0)
                //     {
                //         SendNewLogMessage("[OPEN] BUY — позиция не создана или OpenOrders пуст", LogMessageType.Error);
                //         return;
                //     }
                //
                //     _stopByOrderId[pos.OpenOrders[0].NumberUser] = stopPrice;
                //
                //     SendNewLogMessage(
                //         $"[OPEN] BUY | entry≈{entry:F4} stop={stopPrice:F4} vol={volume}",
                //         LogMessageType.System);
                // }
            }

            // ── SHORT ────────────────────────────────────────────────────────────────
            if (_regime.ValueString == "SHORT-POS")
            {
                // TODO: условие входа в шорт
                // if (lastPrice < signalValue)
                // {
                //     decimal entry = _tab.PriceBestBid;
                //     if (entry <= 0) entry = lastPrice;
                //
                //     decimal stopPrice = /* TODO: ваш уровень стопа */;
                //     if (stopPrice <= 0 || stopPrice <= entry) return;
                //
                //     decimal volume = CalcVolume(Side.Sell, entry, stopPrice);
                //     if (volume <= 0) return;
                //
                //     decimal slippage = entry * (_curSlippagePercent / 100m);
                //     Position pos = _tab.SellAtLimit(volume, entry - slippage);
                //
                //     if (pos == null || pos.OpenOrders == null || pos.OpenOrders.Count == 0)
                //     {
                //         SendNewLogMessage("[OPEN] SELL — позиция не создана или OpenOrders пуст", LogMessageType.Error);
                //         return;
                //     }
                //
                //     _stopByOrderId[pos.OpenOrders[0].NumberUser] = stopPrice;
                //
                //     SendNewLogMessage(
                //         $"[OPEN] SELL | entry≈{entry:F4} stop={stopPrice:F4} vol={volume}",
                //         LogMessageType.System);
                // }
            }
        }

        // ════════════════════════════════════════════════════════════════════════════
        //   ЛОГИКА ЗАКРЫТИЯ — TODO: реализовать
        //
        //   Фаза 1 — трейлинг-уровень ещё хуже цены входа:
        //     Держим фиксированный стоп из SetStopLoss (CloseAtStop).
        //
        //   Фаза 2 — трейлинг-уровень лучше цены входа (прибыль защищена):
        //     Переключаемся на CloseAtTrailingStop, который перезаписывает
        //     фиксированный стоп и двигает его вслед за ценой.
        // ════════════════════════════════════════════════════════════════════════════

        private void LogicClosePosition(List<Candle> candles)
        {
            List<Position> openPositions = _tab.PositionsOpenAll;

            // TODO: получить уровни трейлинга из индикатора
            // decimal trailLong  = /* уровень стопа для лонга  */;
            // decimal trailShort = /* уровень стопа для шорта */;

            for (int i = 0; openPositions != null && i < openPositions.Count; i++)
            {
                Position pos = openPositions[i];
                if (pos.State != PositionStateType.Open) continue;

                // TODO: выбрать нужный трейлинг-уровень по направлению позиции
                // decimal trailLevel = pos.Direction == Side.Buy ? trailLong : trailShort;

                // bool trailIsBetter = pos.Direction == Side.Buy
                //     ? trailLevel > pos.EntryPrice
                //     : trailLevel < pos.EntryPrice;

                // if (trailIsBetter)
                //     _tab.CloseAtTrailingStop(pos, trailLevel, trailLevel);
            }
        }

        // ════════════════════════════════════════════════════════════════════════════
        //   SET STOP LOSS
        //
        //   Вызывается АСИНХРОННО из PositionOpeningSuccesEvent когда позиция
        //   переходит в состояние Open — биржа подтвердила исполнение.
        //
        //   Выставляет НАЧАЛЬНЫЙ стоп по цене, рассчитанной в момент входа.
        //   Далее LogicClosePosition двигает его через CloseAtTrailingStop.
        //
        //   Стоп находится по pos.OpenOrders[0].NumberUser — тому же ключу, под
        //   которым он был сохранён синхронно в LogicOpenPosition после BuyAtLimit.
        // ════════════════════════════════════════════════════════════════════════════

        private void SetStopLoss(Position pos)
        {
            // Защита от двойного срабатывания
            if (pos.StopOrderRedLine > 0)
            {
                SendNewLogMessage($"[STOP] Стоп уже выставлен для pos#{pos.Number}", LogMessageType.System);
                return;
            }

            if (pos.OpenOrders == null || pos.OpenOrders.Count == 0)
            {
                SendNewLogMessage($"[STOP] OpenOrders пуст для pos#{pos.Number} — стоп не выставлен", LogMessageType.Error);
                return;
            }

            int orderKey = pos.OpenOrders[0].NumberUser;

            if (!_stopByOrderId.TryGetValue(orderKey, out decimal stopPrice) || stopPrice <= 0)
            {
                SendNewLogMessage($"[STOP] Стоп не найден для orderKey={orderKey} pos#{pos.Number}", LogMessageType.Error);
                return;
            }

            // Удаляем сразу — при нескольких позициях стопы других ордеров остаются нетронутыми
            _stopByOrderId.Remove(orderKey);

            decimal slippage = stopPrice * (_curSlippagePercent / 100m);

            if (pos.Direction == Side.Buy)
                _tab.CloseAtStop(pos, stopPrice, stopPrice - slippage);
            else
                _tab.CloseAtStop(pos, stopPrice, stopPrice + slippage);

            SendNewLogMessage(
                $"[STOP] pos#{pos.Number} {pos.Direction} | initialStop={stopPrice:F4} orderKey={orderKey}",
                LogMessageType.System);
        }

        // ════════════════════════════════════════════════════════════════════════════
        //   РАСЧЁТ ОБЪЁМА
        //
        //   Объём рассчитывается как риск (% депозита) / (% расстояния до стопа
        //   + проскальзывание + двойная комиссия).
        //   Поддерживаются секции: SPOT/LinearPerpetual, Stocks MOEX,
        //   InversFutures, Futures MOEX.
        //
        //   Вызывать: decimal volume = CalcVolume(Side.Buy, entryPrice, stopPrice);
        // ════════════════════════════════════════════════════════════════════════════

        private decimal CalcVolume(Side side, decimal entryPrice, decimal stopPrice)
        {
            if (_tab?.Security == null || _tab.Security.Lot <= 0) return 0;
            if (entryPrice <= 0 || stopPrice <= 0) return 0;

            _curStopPercent = Math.Abs(entryPrice - stopPrice) / entryPrice * 100m;
            _curStopPrice = stopPrice;

            decimal result = GetVolume(side, entryPrice);

            _curStopPercent = 0;
            _curStopPrice = 0;

            return result;
        }

        /// <summary>
        /// Возвращает количество лотов/контрактов.
        /// Читает _curStopPercent и _curStopPrice — должны быть выставлены вызовом CalcVolume().
        /// </summary>
        private decimal GetVolume(Side side, decimal entryPrice)
        {
            if (_curStopPercent <= 0) return 0;

            decimal balance = GetAssetValue(_tab.Portfolio, _assetNameCurrent.ValueString);
            if (balance <= 0) return 0;

            // Суммарный % риска: стоп + проскальзывание + 2 × комиссия (вход + выход)
            decimal realStopPct = _curStopPercent / 100m
                                + _curSlippagePercent / 100m
                                + _curFeePercent / 100m * 2m;

            decimal riskPct = side == Side.Buy ? _curVolumeLong : _curVolumeShort;
            decimal riskMoney = balance * (riskPct / 100m);
            decimal posSize = riskMoney / realStopPct;   // размер позиции в валюте депозита

            if (posSize < 0.5m) return 0;

            var sec = _tab.Security;
            decimal mult = (decimal)Math.Pow(10, sec.DecimalsVolume);
            decimal qty = 0;

            switch (_modeTrade.ValueString)
            {
                case "SPOT и LinearPerpetual":
                    qty = Math.Floor(posSize / entryPrice * mult) / mult;
                    break;

                case "Stocks MOEX":
                    qty = Math.Floor(posSize / entryPrice / sec.Lot * mult) / mult;
                    break;

                case "InversFutures":
                    qty = Math.Floor(posSize / sec.Lot * mult) / mult;
                    break;

                case "Futures MOEX":
                    if (sec.PriceStep <= 0 || sec.PriceStepCost <= 0 ||
                        sec.MarginBuy <= 0 || _curStopPrice <= 0)
                        return 0;

                    decimal stopPts = Math.Abs(entryPrice - _curStopPrice);
                    decimal lossPerContract = stopPts / sec.PriceStep * sec.PriceStepCost;
                    if (lossPerContract <= 0) return 0;

                    decimal byRisk = Math.Floor(riskMoney / lossPerContract);
                    decimal byGo = Math.Floor(posSize / sec.MarginBuy);
                    qty = Math.Min(byRisk, Math.Min(byGo, 100));
                    break;
            }

            return qty > 0 ? qty : 0;
        }

        // ════════════════════════════════════════════════════════════════════════════
        //   ВСПОМОГАТЕЛЬНЫЙ МЕТОД: стоимость актива в портфеле
        // ════════════════════════════════════════════════════════════════════════════

        private decimal GetAssetValue(Portfolio portfolio, string assetName)
        {
            if (portfolio == null) return 0;
            if (assetName == "Prime") return portfolio.ValueCurrent;

            List<PositionOnBoard> positions = portfolio.GetPositionOnBoard();
            if (positions == null) return 0;

            for (int i = 0; i < positions.Count; i++)
            {
                if (positions[i].SecurityNameCode.Equals(assetName, StringComparison.OrdinalIgnoreCase))
                    return positions[i].ValueCurrent;
            }

            return 0;
        }
    }
}