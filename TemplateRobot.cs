using OsEngine.Entity;
using OsEngine.Indicators;
using OsEngine.Logging;
using OsEngine.OsTrader.Panels;
using OsEngine.OsTrader.Panels.Attributes;
using OsEngine.OsTrader.Panels.Tab;
using System;
using System.Collections.Concurrent;
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
        private StrategyParameterString _tradeLogOnOff;
        // ── Параметры объёма ─────────────────────────────────────────────────────────
        private readonly StrategyParameterString _modeTrade;
        private readonly StrategyParameterString _assetNameCurrent;
        private readonly StrategyParameterDecimal _volumeLong;
        private readonly StrategyParameterDecimal _volumeShort;
        private readonly StrategyParameterDecimal _slippagePercent;
        private readonly StrategyParameterDecimal _feePercent;
        private readonly StrategyParameterInt _bondDaysToMaturity;
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
        private int _curBondDaysToMaturity;
        private int _curTimeZoneUtc;

        // ── Словарь стопов ───────────────────────────────────────────────────────────
        //
        //   Ключ     = order.NumberUser — уникальный int, присваивается движком при создании
        //              ордера ВНУТРИ BuyAtLimit/SellAtLimit, до отправки на биржу.
        //   Значение = начальная цена стопа, рассчитанная для этой конкретной заявки.
        //
        //   Запись добавляется сразу после BuyAtLimit (синхронно, поток свечей).
        //   Запись удаляется в SetStopLoss после выставления начального стопа (асинхронно,
        //   поток событий биржи). ConcurrentDictionary обеспечивает потокобезопасность
        //   без явных lock-ов.
        private readonly ConcurrentDictionary<int, decimal> _stopByOrderId = new ConcurrentDictionary<int, decimal>();

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
             new[] { "SPOT и LinearPerpetual", "InversFutures", "Stocks MOEX", "Futures MOEX", "Bonds MOEX" }, "Base");
            _assetNameCurrent = CreateParameter("Deposit Asset", "USDT",
            new[] { "USDT", "USDC", "USD", "RUB", "EUR", "BTC", "ETH", "XRP", "LTC", "SOL", "Prime" }, "Base");
            _tradeLogOnOff = CreateParameter("Trade debug log", "Off", new[] { "On", "Off" }, "Base");
            _volumeLong = CreateParameter("Volume Long (%)", 2.5m, 0.1m, 50m, 0.1m, "Base");
            _volumeShort = CreateParameter("Volume Short (%)", 2.5m, 0.1m, 50m, 0.1m, "Base");
            _slippagePercent = CreateParameter("Slippage (%)", 0.1m, 0.01m, 2m, 0.01m, "Base");
            _feePercent = CreateParameter("Fee (%)", 0.1m, 0.01m, 1m, 0.01m, "Base");
            _bondDaysToMaturity = CreateParameter("Bond days to maturity", 30, 1, 365, 1, "Base");
            // TODO: создать параметры индикаторов здесь
            // _lengthMyIndicator = CreateParameter("Length", 20, 5, 200, 5, "Indicator");

            // TODO: создать и подключить индикаторы здесь
            // _myIndicator = IndicatorsFactory.CreateIndicatorByName("MyIndicator", name + "MyIndicator", false);
            // _myIndicator = (Aindicator)_tab.CreateCandleIndicator(_myIndicator, "Prime");
            // ((IndicatorParameterInt)_myIndicator.Parameters[0]).ValueInt = _lengthMyIndicator.ValueInt;
            // _myIndicator.Save();

            // Подписки
            ParametrsChangeByUser += OnParametrsChangeByUser;
            _tab.CandleFinishedEvent += _tab_CandleFinishedEvent;
            _tab.PositionOpeningSuccesEvent += SetStopLoss;    // асинхронно, после подтверждения биржи
            _tab.PositionOpeningFailEvent += OnOpeningFail;  // очищаем стоп если ордер не прошёл

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
            _curBondDaysToMaturity = _bondDaysToMaturity.ValueInt;
        }

        // ════════════════════════════════════════════════════════════════════════════
        //   ГЛАВНЫЙ ОБРАБОТЧИК СВЕЧИ
        // ════════════════════════════════════════════════════════════════════════════

        private void _tab_CandleFinishedEvent(List<Candle> candles)
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
                //     _stopByOrderId.TryAdd(pos.OpenOrders[0].NumberUser, stopPrice);
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
                //     _stopByOrderId.TryAdd(pos.OpenOrders[0].NumberUser, stopPrice);
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
        //   Начальный стоп выставляется автоматически в SetStopLoss при открытии.
        //   Здесь можно добавить дополнительные условия выхода по сигналу индикатора:
        //   например, CloseAtMarket или CloseAtLimit при появлении сигнала разворота.
        // ════════════════════════════════════════════════════════════════════════════

        private void LogicClosePosition(List<Candle> candles)
        {
            List<Position> openPositions = _tab.PositionsOpenAll;

            for (int i = 0; openPositions != null && i < openPositions.Count; i++)
            {
                Position pos = openPositions[i];
                if (pos.State != PositionStateType.Open) continue;

                // TODO: добавить условия выхода по сигналу
                // Например:
                // if (pos.Direction == Side.Buy && /* сигнал разворота */ )
                //     _tab.CloseAtMarket(pos, pos.OpenVolume);
            }
        }

        // ════════════════════════════════════════════════════════════════════════════
        //   SET STOP LOSS
        //
        //   Вызывается АСИНХРОННО из PositionOpeningSuccesEvent когда позиция
        //   переходит в состояние Open — биржа подтвердила исполнение.
        //
        //   Выставляет стоп по цене, рассчитанной в момент входа.
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
            _stopByOrderId.TryRemove(orderKey, out _);

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
            decimal stopPercent = Math.Abs(entryPrice - stopPrice) / entryPrice * 100m;
            return GetVolume(side, entryPrice, stopPrice, stopPercent);
        }

        // ════════════════════════════════════════════════════════════════════════════
        //   ОЧИСТКА СТОПА ПРИ НЕУДАЧНОМ ОТКРЫТИИ
        //
        //   Вызывается из PositionOpeningFailEvent — когда ордер на открытие
        //   отклонён биржей или отменён до исполнения. В этом случае SetStopLoss
        //   не вызовется, и запись в _stopByOrderId осталась бы навсегда.
        // ════════════════════════════════════════════════════════════════════════════

        private void OnOpeningFail(Position pos)
        {
            if (pos.OpenOrders == null || pos.OpenOrders.Count == 0)
                return;

            int orderKey = pos.OpenOrders[0].NumberUser;

            if (_stopByOrderId.TryRemove(orderKey, out _))
            {
                SendNewLogMessage(
                    $"[STOP] Стоп удалён из словаря (OpeningFail) orderKey={orderKey} pos#{pos.Number}",
                    LogMessageType.System);
            }
        }

        // Контекст одного вызова GetVolume: накапливает промежуточные значения для лога.
        // Живёт только на стеке — никакого выделения в куче.
        private struct VolumeCalcCtx
        {
            public Side Side;
            public decimal EntryPrice;
            public decimal StopPrice;
            public decimal StopPercent;
            public decimal Balance;
            public decimal RiskPct;
            public decimal RiskMoney;
            public decimal RealStopPct;
            public decimal PosSize;
            public decimal Volume;
            public Security Sec;
            public string RejectReason;
        }

        private decimal GetVolume(Side side, decimal entryPrice, decimal stopPrice, decimal stopPercent)
        {
            VolumeCalcCtx ctx = new VolumeCalcCtx
            {
                Side = side,
                EntryPrice = entryPrice,
                StopPrice = stopPrice,
                StopPercent = stopPercent,
                RejectReason = "ok",
            };

            // --- Входные параметры ---
            if (stopPercent <= 0) return Reject(ref ctx, "stopPercent <= 0");
            if (entryPrice <= 0) return Reject(ref ctx, "entryPrice <= 0");

            // --- Баланс ---
            ctx.Balance = GetAssetValue(_tab.Portfolio, _assetNameCurrent.ValueString);
            if (ctx.Balance <= 0) return Reject(ref ctx, "balance <= 0");

            // --- Риск ---
            ctx.RealStopPct = stopPercent / 100m
                            + _curSlippagePercent / 100m
                            + _curFeePercent / 100m * 2m;
            if (ctx.RealStopPct <= 0) return Reject(ref ctx, "realStopPct <= 0");

            ctx.RiskPct = side == Side.Buy ? _curVolumeLong : _curVolumeShort;
            ctx.RiskMoney = ctx.Balance * (ctx.RiskPct / 100m);
            ctx.PosSize = ctx.RiskMoney / ctx.RealStopPct;

            // --- Инструмент ---
            ctx.Sec = _tab.Security;
            if (ctx.Sec == null) return Reject(ref ctx, "sec == null");

            if (StartProgram != StartProgram.IsOsOptimizer &&
                StartProgram != StartProgram.IsTester)
            {
                if (ctx.Sec.State != SecurityStateType.Activ)
                    return Reject(ref ctx, $"state not active ({ctx.Sec.State})");

                if (ctx.Sec.PriceLimitHigh > 0 && entryPrice > ctx.Sec.PriceLimitHigh)
                    return Reject(ref ctx, $"entryPrice {entryPrice} > PriceLimitHigh {ctx.Sec.PriceLimitHigh}");

                if (ctx.Sec.PriceLimitLow > 0 && entryPrice < ctx.Sec.PriceLimitLow)
                    return Reject(ref ctx, $"entryPrice {entryPrice} < PriceLimitLow {ctx.Sec.PriceLimitLow}");

                if ((ctx.Sec.SecurityType == SecurityType.Futures || ctx.Sec.SecurityType == SecurityType.Option) &&
                    ctx.Sec.Expiration.Year > 1970 &&
                    ctx.Sec.Expiration < DateTime.Now)
                    return Reject(ref ctx, $"instrument expired (Expiration={ctx.Sec.Expiration:yyyy-MM-dd})");

                if (ctx.Sec.SecurityType == SecurityType.Bond &&
                    ctx.Sec.MaturityDate != DateTime.MinValue &&
                    ctx.Sec.MaturityDate < DateTime.Now.AddDays(_curBondDaysToMaturity))
                    return Reject(ref ctx, $"bond maturity too close ({ctx.Sec.MaturityDate:yyyy-MM-dd})");
            }

            // --- Расчёт объёма ---
            decimal mult = ctx.Sec.DecimalsVolume > 0 ? (decimal)Math.Pow(10, ctx.Sec.DecimalsVolume) : 1m;

            switch (_modeTrade.ValueString)
            {
                case "SPOT и LinearPerpetual":
                    if (ctx.Sec.SecurityType != SecurityType.CurrencyPair &&
                        ctx.Sec.SecurityType != SecurityType.Futures &&
                        ctx.Sec.SecurityType != SecurityType.None)
                        return Reject(ref ctx, $"wrong secType for SPOT ({ctx.Sec.SecurityType})");

                    if (ctx.Sec.UsePriceStepCostToCalculateVolume && ctx.Sec.PriceStep > 0 && ctx.Sec.PriceStepCost > 0)
                    {
                        decimal contractCost = entryPrice / ctx.Sec.PriceStep * ctx.Sec.PriceStepCost;
                        if (contractCost <= 0) return Reject(ref ctx, "contractCost <= 0");
                        ctx.Volume = Math.Floor(ctx.PosSize / contractCost * mult) / mult;
                    }
                    else
                    {
                        ctx.Volume = Math.Floor(ctx.PosSize / entryPrice * mult) / mult;
                    }
                    break;

                case "Stocks MOEX":
                    if (ctx.Sec.SecurityType != SecurityType.Stock &&
                        ctx.Sec.SecurityType != SecurityType.Fund &&
                        ctx.Sec.SecurityType != SecurityType.None)
                        return Reject(ref ctx, $"wrong secType for Stocks ({ctx.Sec.SecurityType})");
                    // ВНИМАНИЕ: 'Prime' возвращает суммарную стоимость портфеля (деньги + позиции).
                    // Для расчёта риска нужен только денежный остаток — укажите "RUB".
                    if (_assetNameCurrent.ValueString.Equals("Prime", StringComparison.OrdinalIgnoreCase))
                        return Reject(ref ctx, "asset 'Prime' недопустим для Stocks MOEX — укажите 'RUB' (денежный остаток)");
                    if (ctx.Sec.Lot <= 0) return Reject(ref ctx, "Lot <= 0");

                    ctx.Volume = Math.Floor(ctx.PosSize / entryPrice / ctx.Sec.Lot * mult) / mult;
                    break;

                case "Bonds MOEX":
                    if (ctx.Sec.SecurityType != SecurityType.Bond &&
                        ctx.Sec.SecurityType != SecurityType.None)
                        return Reject(ref ctx, $"wrong secType for Bonds ({ctx.Sec.SecurityType})");
                    // ВНИМАНИЕ: 'Prime' возвращает суммарную стоимость портфеля (деньги + позиции).
                    // Для расчёта риска нужен только денежный остаток — укажите "RUB".
                    if (_assetNameCurrent.ValueString.Equals("Prime", StringComparison.OrdinalIgnoreCase))
                        return Reject(ref ctx, "asset 'Prime' недопустим для Bonds MOEX — укажите 'RUB' (денежный остаток)");
                    if (ctx.Sec.Lot <= 0 || ctx.Sec.NominalCurrent <= 0)
                        return Reject(ref ctx, $"Lot={ctx.Sec.Lot} or NominalCurrent={ctx.Sec.NominalCurrent} <= 0");

                    decimal bondPrice = ctx.Sec.NominalCurrent * entryPrice / 100m;
                    if (bondPrice <= 0) return Reject(ref ctx, "bondPrice <= 0");
                    ctx.Volume = Math.Floor(ctx.PosSize / bondPrice / ctx.Sec.Lot * mult) / mult;
                    break;

                case "InversFutures":
                    if (ctx.Sec.SecurityType != SecurityType.Futures &&
                        ctx.Sec.SecurityType != SecurityType.None)
                        return Reject(ref ctx, $"wrong secType for InversFutures ({ctx.Sec.SecurityType})");
                    if (ctx.Sec.Lot <= 0) return Reject(ref ctx, "Lot <= 0");

                    string selectedAsset = _assetNameCurrent.ValueString.ToUpper();
                    bool isUsdAsset = selectedAsset == "USDT" || selectedAsset == "USDC" ||
                                      selectedAsset == "USD" || selectedAsset == "RUB" ||
                                      selectedAsset == "EUR" || selectedAsset == "PRIME";
                    if (isUsdAsset)
                        return Reject(ref ctx, $"asset '{selectedAsset}' is USD/fiat — укажите базовый крипто-актив (BTC/ETH/...)");

                    decimal posSizeInverse = ctx.Balance * entryPrice * (ctx.RiskPct / 100m) / ctx.RealStopPct;
                    ctx.Volume = Math.Floor(posSizeInverse / ctx.Sec.Lot * mult) / mult;
                    break;

                case "Futures MOEX":
                    if (ctx.Sec.SecurityType != SecurityType.Futures &&
                        ctx.Sec.SecurityType != SecurityType.Option &&
                        ctx.Sec.SecurityType != SecurityType.None)
                        return Reject(ref ctx, $"wrong secType for FuturesMOEX ({ctx.Sec.SecurityType})");

                    // ВНИМАНИЕ: 'Prime' возвращает суммарную стоимость портфеля (деньги + позиции).
                    // Для расчёта риска нужен только денежный остаток — укажите "RUB".
                    if (_assetNameCurrent.ValueString.Equals("Prime", StringComparison.OrdinalIgnoreCase))
                        return Reject(ref ctx, "asset 'Prime' недопустим для Futures MOEX — укажите 'RUB' (денежный остаток)");

                    if (ctx.Sec.PriceStep <= 0 || ctx.Sec.PriceStepCost <= 0 || stopPrice <= 0)
                        return Reject(ref ctx, $"PriceStep={ctx.Sec.PriceStep} PriceStepCost={ctx.Sec.PriceStepCost} stopPrice={stopPrice}");

                    decimal margin = side == Side.Buy ? ctx.Sec.MarginBuy : ctx.Sec.MarginSell;
                    if (margin <= 0)
                        return Reject(ref ctx, $"margin <= 0 (MarginBuy={ctx.Sec.MarginBuy} MarginSell={ctx.Sec.MarginSell})");

                    decimal stopPts = Math.Abs(entryPrice - stopPrice);
                    decimal lossPerContract = stopPts / ctx.Sec.PriceStep * ctx.Sec.PriceStepCost;
                    if (lossPerContract <= 0) return Reject(ref ctx, "lossPerContract <= 0");

                    decimal byRisk = Math.Floor(ctx.RiskMoney / lossPerContract);
                    decimal byGo = Math.Floor(ctx.Balance / margin);
                    ctx.Volume = Math.Min(byRisk, byGo);
                    break;

                default:
                    return Reject(ref ctx, $"unknown mode '{_modeTrade.ValueString}'");
            }

            if (ctx.Volume <= 0) return Reject(ref ctx, "volume <= 0 after calculation");

            // --- Округление по шагу объёма ---
            if (ctx.Sec.VolumeStep > 0)
                ctx.Volume = Math.Floor(ctx.Volume / ctx.Sec.VolumeStep) * ctx.Sec.VolumeStep;

            // --- Проверка минимального объёма ---
            if (ctx.Sec.MinTradeAmount > 0)
            {
                decimal minVolume = ctx.Sec.MinTradeAmountType == MinTradeAmountType.C_Currency
                    ? ctx.Sec.MinTradeAmount / entryPrice
                    : ctx.Sec.MinTradeAmount;

                if (ctx.Volume < minVolume)
                    return Reject(ref ctx, $"volume={ctx.Volume} < minVolume={minVolume} (MinTradeAmount={ctx.Sec.MinTradeAmount} type={ctx.Sec.MinTradeAmountType})");
            }

            return LogVolume(ref ctx);
        }

        // Помечает контекст как отклонённый, логирует и возвращает 0.
        private decimal Reject(ref VolumeCalcCtx ctx, string reason)
        {
            ctx.Volume = 0;
            ctx.RejectReason = reason;
            return LogVolume(ref ctx);
        }

        private decimal LogVolume(ref VolumeCalcCtx ctx)
        {
            if (_tradeLogOnOff.ValueString == "On")
            {
                Security s = ctx.Sec;
                string log =
                $@" -GET VOLUME DEBUG
                SECURITY               = {s?.Name} | TYPE = {s?.SecurityType} | STATE = {s?.State}
                MODE                   = {_modeTrade.ValueString}
                SIDE                   = {ctx.Side}
                ------ ASSET / BALANCE ------
                ASSET                  = {_assetNameCurrent.ValueString}
                BALANCE                = {ctx.Balance:F6}
                ------ RISK ------
                RISK PCT               = {ctx.RiskPct:F4} %
                RISK MONEY             = {ctx.RiskMoney:F6}
                STOP %                 = {ctx.StopPercent:F6} %
                SLIPPAGE %             = {_curSlippagePercent:F4} %
                FEE %                  = {_curFeePercent:F4} %
                REAL STOP PCT          = {ctx.RealStopPct:F6}
                POS SIZE               = {ctx.PosSize:F6}
                ------ PRICE ------
                ENTRY PRICE            = {ctx.EntryPrice:F4}
                STOP PRICE             = {ctx.StopPrice:F4}
                ------ INSTRUMENT ------
                LOT                    = {s?.Lot}
                DECIMALS VOL           = {s?.DecimalsVolume}
                VOLUME STEP            = {s?.VolumeStep}
                MIN TRADE AMOUNT       = {s?.MinTradeAmount} ({s?.MinTradeAmountType})
                PRICE STEP             = {s?.PriceStep}
                STEP COST              = {s?.PriceStepCost}
                EXPIRATION             = {s?.Expiration:yyyy-MM-dd}
                MARGIN BUY             = {s?.MarginBuy}
                MARGIN SELL            = {s?.MarginSell}
                ------ RESULT ------
                VOLUME                 = {ctx.Volume}
                REJECT REASON          = {ctx.RejectReason}
                -";

                SendNewLogMessage(log, Logging.LogMessageType.System);
            }

            return ctx.Volume;
        }

        private decimal GetAssetValue(Portfolio portfolio, string assetName)
        {
            if (portfolio == null) return 0;

            // Prime = суммарная стоимость портфеля в валюте счёта (деньги + открытые позиции).
            // Подходит для SPOT / LinearPerpetual, где баланс и позиции в одной валюте.
            // Для MOEX (Stocks, Bonds, Futures) использовать нельзя: нужен денежный остаток ("RUB"),
            // а не суммарная стоимость счёта вместе с позициями.
            // Для InversFutures указывать конкретный базовый актив (например BTC),
            // так как формула умножает balance на entryPrice.
            if (assetName == "Prime") return portfolio.ValueCurrent;

            List<PositionOnBoard> positions = portfolio.GetPositionOnBoard();
            if (positions == null) return 0;

            foreach (PositionOnBoard p in positions)
            {
                if (p.SecurityNameCode.Equals(assetName, StringComparison.OrdinalIgnoreCase))
                    return p.ValueCurrent;
            }

            return 0;
        }
    }
}