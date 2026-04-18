# TemplateRobotAdvanced — Продвинутый шаблон торгового робота для OsEngine

Готовый шаблон для создания собственного торгового робота на платформе [OsEngine](https://github.com/AlexWan/OsEngine). Содержит полную инфраструктуру: управление объёмом с учётом риска, стоп-лосс, тейк-профит, трейлинг-стоп, неторговые периоды, все события `BotTabSimple`. Вам остаётся только добавить свою торговую логику.

---

## Содержание

- [Что уже реализовано](#что-уже-реализовано)
- [Быстрый старт](#быстрый-старт)
- [Структура файла](#структура-файла)
- [Параметры робота](#параметры-робота)
- [Расчёт объёма](#расчёт-объёма)
- [Стоп, тейк и трейлинг без гонки состояний](#стоп-тейк-и-трейлинг-без-гонки-состояний)
- [Неторговые периоды](#неторговые-периоды)
- [События BotTabSimple](#события-bottabsimple)
- [Свойства Position](#свойства-position)
- [Пошаговое руководство по добавлению стратегии](#пошаговое-руководство-по-добавлению-стратегии)
- [Режимы работы (Regime)](#режимы-работы-regime)
- [Требования](#требования)
- [Автор](#автор)

---

## Что уже реализовано

| Функциональность | Статус |
|---|---|
| Расчёт объёма по % риска депозита | ✅ Готово |
| Поддержка SPOT / Linear Perpetual | ✅ Готово |
| Поддержка Stocks MOEX | ✅ Готово |
| Поддержка Futures MOEX | ✅ Готово |
| Поддержка Inverse Futures | ✅ Готово |
| Поддержка Bonds MOEX | ✅ Готово |
| Стоп-лосс (CloseAtStop) | ✅ Готово |
| Тейк-профит (CloseAtProfit) | ✅ Готово |
| Трейлинг-стоп (CloseAtTrailingStop) | ✅ Заготовка |
| Неторговые периоды (по времени и дням) | ✅ Готово |
| Очистка словаря стопов при неудачном открытии | ✅ Готово |
| Защита от двойного выставления стопа | ✅ Готово |
| Защита от двойного ордера закрытия (`CloseActive`) | ✅ Готово |
| Все события BotTabSimple (19 штук) | ✅ Подключены |
| Синхронизация параметров GUI | ✅ Готово |
| Подробный лог расчёта объёма | ✅ Готово |
| **Торговая логика (сигналы входа/выхода)** | ❌ Ваша реализация |

---

## Быстрый старт

### 1. Скопируйте файл в проект OsEngine

```
OsEngine/
└── OsEngine/
    └── Robots/
        └── TemplateRobotAdvanced.cs   ← сюда
```

### 2. Переименуйте класс под свою стратегию

Выполните замену во всём файле:

| Было | Станет |
|---|---|
| `TemplateRobotAdvanced` | `MyStrategyRobot` |
| `"TemplateRobotAdvanced"` | `"MyStrategyRobot"` |

```csharp
// Было:
[Bot("TemplateRobotAdvanced")]
public class TemplateRobotAdvanced : BotPanel

// Стало:
[Bot("MyStrategyRobot")]
public class MyStrategyRobot : BotPanel
```

### 3. Реализуйте свою торговую логику

Все места, требующие вашего кода, отмечены комментарием `// TODO:`.

Найти их быстро:
- **VS Code:** `Ctrl+Shift+F` → ввести `TODO`
- **Visual Studio:** `Ctrl+F` → ввести `TODO`

### 4. Скомпилируйте и запустите OsEngine

Соберите проект и выберите своего робота в интерфейсе OsEngine.

---

## Структура файла

```
TemplateRobotAdvanced.cs
│
├── КОНСТРУКТОР
│   ├── Инициализация неторговых периодов
│   ├── Регистрация параметров GUI (Base + Exit)
│   ├── Подписка на все события BotTabSimple
│   ├── TODO: создать параметры индикаторов
│   └── TODO: создать и подключить индикаторы
│
├── СОБЫТИЯ РЫНОЧНЫХ ДАННЫХ
│   ├── _tab_CandleFinishedEvent     — закрытая свеча (главный поток)
│   ├── _tab_CandleUpdateEvent       — обновление текущей свечи по тику
│   ├── _tab_NewTickEvent            — каждый тик
│   ├── _tab_BestBidAskChangeEvent   — лучший бид/аск
│   ├── _tab_MarketDepthUpdateEvent  — стакан
│   ├── _tab_ServerTimeChangeEvent   — время сервера
│   ├── _tab_FirstTickToDayEvent     — первый тик нового дня
│   └── _tab_PortfolioOnExchangeChangedEvent — изменение портфеля
│
├── СОБЫТИЯ ТЕХНИЧЕСКИЕ
│   ├── _tab_SecuritySubscribeEvent  — подписка на инструмент
│   ├── _tab_OrderUpdateEvent        — обновление ордера
│   ├── _tab_CancelOrderFailEvent    — не удалось отменить ордер
│   ├── _tab_MyTradeEvent            — своя сделка
│   └── _tab_IndicatorUpdateEvent    — пересчёт индикатора
│
├── ГЛАВНЫЙ ОБРАБОТЧИК СВЕЧИ (_tab_CandleFinishedEvent)
│   ├── Проверка режима (Regime)
│   ├── Проверка неторгового времени
│   ├── LogicClosePosition — логика закрытия
│   └── LogicOpenPosition  — логика открытия
│
├── LogicOpenPosition    ← TODO: ваши сигналы входа
├── LogicClosePosition   ← TODO: выход по сигналу + трейлинг + аварийный выход
│
├── СОБЫТИЯ ПОЗИЦИИ
│   ├── OnPositionOpeningSucces      — выставить стоп + тейк
│   ├── OnPositionOpeningFail        — очистить словарь
│   ├── OnPositionClosingSucces      — логика после закрытия
│   ├── OnPositionClosingFail        — повторная попытка закрыть
│   ├── OnPositionStopActivate       — стоп сработал
│   ├── OnPositionProfitActivate     — тейк сработал
│   ├── OnPositionBuyAtStopActivate  — BuyAtStop активирован
│   ├── OnPositionSellAtStopActivate — SellAtStop активирован
│   └── OnPositionNetVolumeChange    — частичное исполнение
│
└── РАСЧЁТ ОБЪЁМА
    ├── CalcVolume(side, entryPrice, stopPrice)
    ├── GetVolume(...)     — основная логика по всем секциям
    ├── LogVolume(...)     — подробный лог (Trade debug log = On)
    └── GetAssetValue(...) — баланс по конкретному активу
```

---

## Параметры робота

### Вкладка "Base"

| Параметр | Тип | По умолчанию | Описание |
|---|---|---|---|
| `Non trade periods` | button | — | Открыть диалог настройки неторговых периодов |
| `Regime` | string | `Off` | Режим работы робота |
| `Time zone UTC` | int | `4` | Часовой пояс для неторговых периодов |
| `Trade debug log` | string | `Off` | Подробный лог расчёта объёма (`On` / `Off`) |
| `Trade Section` | string | `SPOT и LinearPerpetual` | Тип торгуемого инструмента |
| `Deposit Asset` | string | `USDT` | Валюта депозита для расчёта риска |
| `Volume Long (%)` | decimal | `2.5` | Риск на одну сделку лонг, % от депозита |
| `Volume Short (%)` | decimal | `2.5` | Риск на одну сделку шорт, % от депозита |
| `Min LOT (Tester)` | decimal | `0` | Минимальный объём в тестере/оптимизаторе (0 = не проверять) |
| `Slippage (%)` | decimal | `0.1` | Проскальзывание, % от цены входа |
| `Fee (%)` | decimal | `0.1` | Комиссия биржи, % (учитывается дважды: вход + выход) |
| `Bond days to maturity` | int | `30` | Минимум дней до погашения облигации для входа |

### Вкладка "Exit"

| Параметр | Тип | По умолчанию | Описание |
|---|---|---|---|
| `Stop (%)` | decimal | `1.0` | Фиксированный стоп, % от цены входа |
| `Profit (%, 0=off)` | decimal | `0` | Фиксированный тейк, % от цены входа (`0` = не использовать) |
| `Use Trailing Stop` | bool | `false` | Включить трейлинг-стоп |

> **Про фиксированный стоп/тейк:** используйте параметры вкладки Exit если вам нужен простой % от входа. Если стоп рассчитывается динамически (по индикатору, свингу, ATR) — передавайте цену напрямую в `CalcVolume()` и `CloseAtStop()`, параметры не нужны.

---

## Расчёт объёма

Объём рассчитывается так, чтобы потенциальный убыток при срабатывании стопа не превышал заданный % депозита.

**Формула:**

```
Риск в деньгах  = Баланс × (Volume% / 100)
Реальный стоп % = Расстояние до стопа% + Проскальзывание% + Комиссия% × 2
Размер позиции  = Риск в деньгах / Реальный стоп%
```

**Как вызывать:**

```csharp
decimal entry     = _tab.PriceBestAsk;
decimal stopPrice = entry * (1m - _curStopPercent / 100m);  // фиксированный стоп
// или:
// decimal stopPrice = /* ваш уровень: индикатор, свинг, ATR */;

decimal volume = CalcVolume(Side.Buy, entry, stopPrice);
if (volume <= 0) return;  // риск-менеджер не пропускает сделку

decimal slippage = entry * (_curSlippagePercent / 100m);
Position pos = _tab.BuyAtLimit(volume, entry + slippage);

decimal profitPrice = _curProfitPercent > 0
    ? entry * (1m + _curProfitPercent / 100m)
    : 0m;

_stopByOrderId.TryAdd(pos.OpenOrders[0].NumberUser,
    new StopProfitPair { StopPrice = stopPrice, ProfitPrice = profitPrice });
```

### Поддерживаемые секции

| Секция | Инструменты |
|---|---|
| `SPOT и LinearPerpetual` | Крипто-спот, линейные перпетуалы (Bybit, Binance и др.) |
| `Stocks MOEX` | Акции и фонды Московской биржи |
| `Futures MOEX` | Фьючерсы MOEX (с учётом ГО и шага цены) |
| `InversFutures` | Инверсные фьючерсы (BitMEX, Bybit Inverse) |
| `Bonds MOEX` | Облигации MOEX |

---

## Стоп, тейк и трейлинг без гонки состояний

### Проблема

OsEngine работает в двух потоках одновременно:

- **Синхронный поток** — `CandleFinishedEvent`, где выставляется ордер (`BuyAtLimit`)
- **Асинхронный поток** — `PositionOpeningSuccesEvent`, когда биржа подтвердила исполнение

Если хранить цену стопа в поле класса (`_stopPrice`), при нескольких одновременных позициях значение перезапишется — это **гонка состояний (race condition)**. Стоп выставляется по неверной цене или не выставляется вовсе.

### Решение — `ConcurrentDictionary<int, StopProfitPair>`

Шаблон хранит пару {стоп, тейк} в `ConcurrentDictionary`, где **ключ — `NumberUser` ордера на вход**. `ConcurrentDictionary` обеспечивает потокобезопасность без явных `lock`-ов.

```
Синхронно (CandleFinishedEvent)          Асинхронно (PositionOpeningSuccesEvent)
─────────────────────────────────        ─────────────────────────────────────────
1. Рассчитать stopPrice, profitPrice     4. Биржа подтвердила исполнение
2. BuyAtLimit → получить pos             5. OnPositionOpeningSucces(pos) вызван
3. Записать в словарь:                   6. TryRemove(orderKey) — УДАЛЯЕТ ключ
   _stopByOrderId.TryAdd(                7. CloseAtStop(pos, stopPrice, ...)
     pos.OpenOrders[0].NumberUser,       8. CloseAtProfit(pos, profitPrice, ...)
     new StopProfitPair { ... })
```

`NumberUser` присваивается движком **до отправки на биржу**, поэтому он гарантированно доступен синхронно и уникален для каждой заявки.

### Защита от двойного ордера закрытия

В `LogicClosePosition` перед любым `CloseAt*` всегда стоит проверка:

```csharp
if (pos.CloseActive) continue;  // уже есть активный ордер закрытия — пропускаем
```

Без этой проверки при быстром рынке можно послать второй `CloseAtMarket` пока первый ещё в полёте.

### Защита от двойного выставления стопа

`OnPositionOpeningSucces` проверяет `pos.StopOrderRedLine > 0` — если стоп уже выставлен (событие сработало дважды), повторная установка игнорируется:

```csharp
if (pos.StopOrderRedLine > 0)
{
    SendNewLogMessage($"[STOP] Стоп уже выставлен для pos#{pos.Number}", LogMessageType.System);
    return;
}
```

### Очистка словаря при неудачном открытии

Если ордер на открытие отклонён биржей, `PositionOpeningSuccesEvent` не вызывается. Чтобы запись в словаре не осталась навсегда, предусмотрен `OnPositionOpeningFail`:

```csharp
private void OnPositionOpeningFail(Position pos)
{
    if (pos.OpenOrders == null || pos.OpenOrders.Count == 0) return;
    int orderKey = pos.OpenOrders[0].NumberUser;
    if (_stopByOrderId.TryRemove(orderKey, out _))
        SendNewLogMessage($"[STOP] Ключ удалён (OpeningFail) orderKey={orderKey}", LogMessageType.System);
}
```

### Трейлинг-стоп

Заготовка находится в `LogicClosePosition`. Включите параметр `Use Trailing Stop` и задайте уровень:

```csharp
// CloseAtTrailingStop двигает стоп только в прибыльную сторону.
// Вызывайте каждый бар — движок сам решит, нужно ли обновлять.
if (_curUseTrailingStop)
{
    decimal lastLow  = candles[candles.Count - 1].Low;
    decimal lastHigh = candles[candles.Count - 1].High;
    decimal slippage = candles[candles.Count - 1].Close * (_curSlippagePercent / 100m);

    if (pos.Direction == Side.Buy)
        _tab.CloseAtTrailingStop(pos, lastLow, lastLow - slippage);
    else
        _tab.CloseAtTrailingStop(pos, lastHigh, lastHigh + slippage);
}
```

### Итог

| Подход | Гонка состояний | Несколько позиций | Утечка при неудаче |
|---|---|---|---|
| Поле класса `_stopPrice` | ❌ Есть | ❌ Стопы перемешиваются | — |
| `Dictionary<int, decimal>` | ⚠️ Нужен `lock` | ✅ Каждая позиция — свой стоп | ❌ Без `OnOpeningFail` |
| `ConcurrentDictionary` + `TryRemove` + `OnOpeningFail` | ✅ Нет | ✅ Каждая позиция — свой стоп | ✅ Нет |

---

## Неторговые периоды

В конструкторе заданы периоды по умолчанию (можно изменить через кнопку в GUI):

| Период | Время (UTC+4) | Статус |
|---|---|---|
| Ночной | 00:00 — 10:05 | Включён |
| Обеденный | 13:54 — 14:06 | Выключен |
| Вечерний | 18:01 — 23:58 | Включён |
| Суббота | — | Не торгуем |
| Воскресенье | — | Не торгуем |

Настройки сохраняются между запусками и могут быть изменены через диалог прямо во время работы робота.

---

## События BotTabSimple

Шаблон подключает все события `BotTabSimple`. Каждый обработчик задокументирован и готов к заполнению. Ненужные события удалите из конструктора, чтобы не тратить ресурсы.

### События торговли

| Событие | Метод | Когда вызывается |
|---|---|---|
| `CandleFinishedEvent` | `_tab_CandleFinishedEvent` | Закрытие свечи — основной торговый поток |
| `CandleUpdateEvent` | `_tab_CandleUpdateEvent` | Тиковое обновление текущей свечи |
| `PositionOpeningSuccesEvent` | `OnPositionOpeningSucces` | Позиция открыта (биржа подтвердила) |
| `PositionOpeningFailEvent` | `OnPositionOpeningFail` | Ордер открытия отклонён |
| `PositionClosingSuccesEvent` | `OnPositionClosingSucces` | Позиция закрыта |
| `PositionClosingFailEvent` | `OnPositionClosingFail` | Ордер закрытия не прошёл |
| `PositionStopActivateEvent` | `OnPositionStopActivate` | `CloseAtStop` сработал |
| `PositionProfitActivateEvent` | `OnPositionProfitActivate` | `CloseAtProfit` сработал |
| `PositionBuyAtStopActivateEvent` | `OnPositionBuyAtStopActivate` | `BuyAtStop` активирован |
| `PositionSellAtStopActivateEvent` | `OnPositionSellAtStopActivate` | `SellAtStop` активирован |
| `PositionNetVolumeChangeEvent` | `OnPositionNetVolumeChange` | Частичное исполнение ордера |

### События рыночных данных

| Событие | Метод | Когда вызывается |
|---|---|---|
| `NewTickEvent` | `_tab_NewTickEvent` | Каждый тик (высокая частота) |
| `BestBidAskChangeEvent` | `_tab_BestBidAskChangeEvent` | Изменение лучшего бида/аска |
| `MarketDepthUpdateEvent` | `_tab_MarketDepthUpdateEvent` | Обновление стакана |
| `ServerTimeChangeEvent` | `_tab_ServerTimeChangeEvent` | Изменение серверного времени |
| `FirstTickToDayEvent` | `_tab_FirstTickToDayEvent` | Первый тик нового дня |
| `PortfolioOnExchangeChangedEvent` | `_tab_PortfolioOnExchangeChangedEvent` | Изменение портфеля |
| `MyTradeEvent` | `_tab_MyTradeEvent` | Своя сделка исполнена |
| `OrderUpdateEvent` | `_tab_OrderUpdateEvent` | Смена статуса ордера |
| `CancelOrderFailEvent` | `_tab_CancelOrderFailEvent` | Не удалось отменить ордер |
| `SecuritySubscribeEvent` | `_tab_SecuritySubscribeEvent` | Подписка на инструмент |
| `IndicatorUpdateEvent` | `_tab_IndicatorUpdateEvent` | Пересчёт индикатора |

---

## Свойства Position

В `LogicClosePosition` и обработчиках событий доступны все свойства открытой позиции:

| Свойство | Тип | Описание |
|---|---|---|
| `pos.Number` | `int` | Уникальный номер позиции |
| `pos.Direction` | `Side` | `Buy` / `Sell` |
| `pos.State` | `PositionStateType` | Текущий статус (см. ниже) |
| `pos.OpenVolume` | `decimal` | Текущий открытый объём → передавать в `CloseAt*` |
| `pos.MaxVolume` | `decimal` | Максимальный объём при открытии |
| `pos.EntryPrice` | `decimal` | Средняя цена входа по исполненным сделкам |
| `pos.ClosePrice` | `decimal` | Средняя цена закрытия |
| `pos.ProfitPortfolioPercent` | `decimal` | PnL в % от портфеля (с учётом комиссии) |
| `pos.ProfitPortfolioAbs` | `decimal` | PnL в деньгах (с учётом комиссии и шага цены) |
| `pos.ProfitOperationPercent` | `decimal` | PnL в % от цены входа (без комиссии) |
| `pos.ProfitOperationAbs` | `decimal` | PnL в пунктах (без комиссии) |
| `pos.StopOrderRedLine` | `decimal` | Уровень активации стопа (`> 0` если выставлен) |
| `pos.ProfitOrderRedLine` | `decimal` | Уровень активации тейка (`> 0` если выставлен) |
| `pos.StopOrderIsActive` | `bool` | Стоп активен |
| `pos.ProfitOrderIsActive` | `bool` | Тейк активен |
| `pos.CloseActive` | `bool` | Есть активный ордер на закрытие |
| `pos.OpenActive` | `bool` | Есть активный ордер на открытие |
| `pos.TimeOpen` | `DateTime` | Время открытия позиции |
| `pos.TimeClose` | `DateTime` | Время закрытия |
| `pos.Comment` | `string` | Произвольное поле для ваших данных |
| `pos.SignalTypeOpen` | `string` | Метка сигнала открытия |
| `pos.SignalTypeClose` | `string` | Метка сигнала закрытия |
| `pos.OpenOrders` | `List<Order>` | Ордера на открытие |
| `pos.CloseOrders` | `List<Order>` | Ордера на закрытие |
| `pos.MyTrades` | `List<MyTrade>` | Все сделки по позиции |

### Статусы позиции (`PositionStateType`)

```
None         — создана, ещё ничего не отправлено
Opening      — ордер на открытие отправлен
Open         — позиция открыта (биржа подтвердила)
Closing      — ордер на закрытие отправлен
Done         — позиция закрыта
OpeningFail  — открывающий ордер отклонён
ClosingFail  — закрывающий ордер не прошёл
Deleted      — удалена
```

> ⚠️ В `LogicClosePosition` всегда проверяйте `pos.State == PositionStateType.Open` и `pos.CloseActive == false` — иначе попадёте на позиции в процессе закрытия.

### Устаревшие свойства — не использовать

| Устаревшее | Правильное |
|---|---|
| `ProfitPortfolioPersent` | `ProfitPortfolioPercent` |
| `ProfitPortfolioPunkt` | `ProfitPortfolioAbs` |
| `ProfitOperationPersent` | `ProfitPortfolioPercent` |
| `StopOrderIsActiv` | `StopOrderIsActive` |
| `ProfitOrderIsActiv` | `ProfitOrderIsActive` |
| `CloseActiv` | `CloseActive` |
| `OpenActiv` | `OpenActive` |

---

## Пошаговое руководство по добавлению стратегии

### Шаг 1 — Добавить параметры индикатора

В разделе `// TODO: добавить параметры индикаторов здесь`:

```csharp
private readonly StrategyParameterInt _lengthEma;
private readonly StrategyParameterInt _lengthRsi;
```

В конструкторе:

```csharp
_lengthEma = CreateParameter("EMA Length", 20, 5, 200, 5, "Indicator");
_lengthRsi = CreateParameter("RSI Length", 14, 5, 100, 1, "Indicator");
```

### Шаг 2 — Создать и подключить индикаторы

```csharp
private Aindicator _ema;
private Aindicator _rsi;
```

В конструкторе:

```csharp
_ema = IndicatorsFactory.CreateIndicatorByName("Ema", name + "Ema", false);
_ema = (Aindicator)_tab.CreateCandleIndicator(_ema, "Prime");
((IndicatorParameterInt)_ema.Parameters[0]).ValueInt = _lengthEma.ValueInt;
_ema.Save();

_rsi = IndicatorsFactory.CreateIndicatorByName("RSI", name + "RSI", false);
_rsi = (Aindicator)_tab.CreateCandleIndicator(_rsi, "Second");
((IndicatorParameterInt)_rsi.Parameters[0]).ValueInt = _lengthRsi.ValueInt;
_rsi.Save();
```

### Шаг 3 — Обновлять параметры при изменении в GUI

В методе `OnParametrsChangeByUser()`:

```csharp
((IndicatorParameterInt)_ema.Parameters[0]).ValueInt = _lengthEma.ValueInt;
_ema.Save();
_ema.Reload();

((IndicatorParameterInt)_rsi.Parameters[0]).ValueInt = _lengthRsi.ValueInt;
_rsi.Save();
_rsi.Reload();
```

### Шаг 4 — Реализовать сигнал входа

В `LogicOpenPosition()` раскомментируйте и адаптируйте заготовку. Пример для лонга:

```csharp
decimal emaValue = _ema.DataSeries[0].Last;
decimal rsiValue = _rsi.DataSeries[0].Last;
if (emaValue <= 0 || rsiValue <= 0) return;

if (lastPrice > emaValue && rsiValue < 70)
{
    decimal entry     = _tab.PriceBestAsk;
    if (entry <= 0) entry = lastPrice;

    decimal stopPrice = emaValue * 0.99m;  // стоп под EMA

    decimal volume = CalcVolume(Side.Buy, entry, stopPrice);
    if (volume <= 0) return;

    decimal slippage = entry * (_curSlippagePercent / 100m);
    Position pos = _tab.BuyAtLimit(volume, entry + slippage, "EMA+RSI");

    if (pos == null || pos.OpenOrders == null || pos.OpenOrders.Count == 0)
    {
        SendNewLogMessage("[OPEN] BUY — позиция не создана", LogMessageType.Error);
        return;
    }

    decimal profitPrice = _curProfitPercent > 0
        ? entry * (1m + _curProfitPercent / 100m)
        : 0m;

    _stopByOrderId.TryAdd(pos.OpenOrders[0].NumberUser,
        new StopProfitPair { StopPrice = stopPrice, ProfitPrice = profitPrice });

    SendNewLogMessage(
        $"[OPEN] BUY | entry≈{entry:F4} stop={stopPrice:F4} profit={profitPrice:F4} vol={volume}",
        LogMessageType.System);
}
```

### Шаг 5 — Добавить условия выхода по сигналу (опционально)

Начальный стоп и тейк выставляются автоматически. В `LogicClosePosition` добавьте выход по индикатору:

```csharp
decimal emaValue = _ema.DataSeries[0].Last;
decimal lastPrice = candles[candles.Count - 1].Close;

for (int i = 0; openPositions != null && i < openPositions.Count; i++)
{
    Position pos = openPositions[i];
    if (pos.State != PositionStateType.Open) continue;
    if (pos.CloseActive) continue;

    if (pos.Direction == Side.Buy && lastPrice < emaValue)
        _tab.CloseAtMarket(pos, pos.OpenVolume, "EmaClose");

    if (pos.Direction == Side.Sell && lastPrice > emaValue)
        _tab.CloseAtMarket(pos, pos.OpenVolume, "EmaClose");
}
```

---

## Режимы работы (Regime)

| Значение | Поведение |
|---|---|
| `Off` | Робот полностью остановлен |
| `On` | Разрешены лонг и шорт |
| `LONG-POS` | Только лонг (`SHORT-POS` пропускается) |
| `SHORT-POS` | Только шорт (`LONG-POS` пропускается) |
| `CLOSE-POS` | Только закрывает существующие позиции, новых не открывает |

> В шаблоне режимы реализованы через `!= "SHORT-POS"` и `!= "LONG-POS"`, что означает: режим `On` открывает оба направления, `LONG-POS` блокирует блок шорта, `SHORT-POS` блокирует блок лонга.

---

## Требования

- [OsEngine](https://github.com/AlexWan/OsEngine) актуальной версии
- .NET Framework / .NET совместимый с вашей версией OsEngine
- C# 7.0+
- `using System.Collections.Concurrent` — используется для `ConcurrentDictionary`

---

## Автор

Разработано трейдером **SidorenkoVA** — [@si0683](https://t.me/si0683)

---

## Лицензия

Шаблон предоставляется "как есть". Используйте и модифицируйте свободно.

> **Важно:** Торговые роботы несут финансовые риски. Всегда тестируйте стратегию на исторических данных и в режиме Paper Trading перед реальной торговлей.

---

`osengine` `trading-bot` `algorithmic-trading` `csharp` `dotnet` `template` `quant` `robot` `strategy` `stop-loss` `take-profit` `trailing-stop` `risk-management` `moex` `futures` `spot` `bybit` `binance` `trading-robot` `boilerplate` `osengine-robot` `osengine-template`
