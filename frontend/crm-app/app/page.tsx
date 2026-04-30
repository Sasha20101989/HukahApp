"use client";

import { CalendarClock, ClipboardList, Flame, LayoutDashboard, PackageSearch, Plus, Search, UsersRound } from "lucide-react";
import { useEffect, useMemo, useState } from "react";
import { getFromGateway } from "../lib/api";

const orders = [
  { id: "A-104", time: "20:05", table: "Стол 1", mix: "Berry Ice", master: "Иван", status: "PREPARING" },
  { id: "A-105", time: "20:12", table: "Стол 2", mix: "Dark Citrus", master: "Мария", status: "READY" },
  { id: "A-106", time: "20:20", table: "VIP 1", mix: "Sweet Fresh", master: "Иван", status: "SMOKING" }
];

const tables = [
  { name: "Стол 1", x: 14, y: 16, status: "busy", capacity: 4 },
  { name: "Стол 2", x: 48, y: 18, status: "busy", capacity: 6 },
  { name: "Стол 3", x: 18, y: 58, status: "free", capacity: 4 },
  { name: "VIP 1", x: 62, y: 56, status: "busy", capacity: 8 }
];

const stock = [
  { name: "Darkside Strawberry", grams: 42, min: 50 },
  { name: "Musthave Mint", grams: 118, min: 50 },
  { name: "Element Blueberry", grams: 74, min: 50 }
];

const bookings = [
  { time: "21:00", guest: "Александр", table: "Стол 3", status: "CONFIRMED" },
  { time: "21:30", guest: "Екатерина", table: "VIP 1", status: "WAITING_PAYMENT" }
];

const mixes = [
  { name: "Berry Ice", strength: "MEDIUM", margin: 724 },
  { name: "Dark Citrus", strength: "STRONG", margin: 790 },
  { name: "Sweet Fresh", strength: "LIGHT", margin: 680 }
];

export default function CrmDashboard() {
  const [branch, setBranch] = useState("center");
  const [query, setQuery] = useState("");
  const [floorTables, setFloorTables] = useState(tables);
  const [stockRows, setStockRows] = useState(stock);
  const [bookingRows, setBookingRows] = useState(bookings);

  useEffect(() => {
    let active = true;

    async function loadOperationalData() {
      try {
        const [floorPlan, inventory, bookingList] = await Promise.all([
          getFromGateway<FloorPlan>("/api/branches/10000000-0000-0000-0000-000000000001/floor-plan"),
          getFromGateway<InventoryItem[]>("/api/inventory?branchId=10000000-0000-0000-0000-000000000001&lowStockOnly=false"),
          getFromGateway<Booking[]>("/api/bookings?branchId=10000000-0000-0000-0000-000000000001")
        ]);

        if (!active) {
          return;
        }

        setFloorTables(floorPlan.tables.map((table) => ({
          name: table.name,
          x: Number(table.xPosition) / 5,
          y: Number(table.yPosition) / 5,
          status: table.status === "FREE" ? "free" : "busy",
          capacity: table.capacity
        })));
        setStockRows(inventory.map((item) => ({
          name: item.tobaccoId.slice(0, 8),
          grams: item.stockGrams,
          min: item.minStockGrams
        })));
        setBookingRows(bookingList.map((booking) => ({
          time: new Date(booking.startTime).toLocaleTimeString("ru-RU", { hour: "2-digit", minute: "2-digit" }),
          guest: booking.clientId.slice(0, 8),
          table: booking.tableId.slice(0, 8),
          status: booking.status
        })));
      } catch {
        // Keep local demo data when backend is not running.
      }
    }

    loadOperationalData();
    return () => {
      active = false;
    };
  }, []);

  const filteredOrders = useMemo(
    () => orders.filter((order) => `${order.id} ${order.table} ${order.mix}`.toLowerCase().includes(query.toLowerCase())),
    [query]
  );

  return (
    <main className="shell">
      <aside className="sidebar">
        <div className="brand">
          <span className="brand-mark"><Flame size={20} /></span>
          Hookah CRM
        </div>
        <nav className="nav" aria-label="CRM sections">
          <button className="active"><LayoutDashboard size={18} />Зал</button>
          <button><ClipboardList size={18} />Заказы</button>
          <button><PackageSearch size={18} />Склад</button>
          <button><UsersRound size={18} />Персонал</button>
        </nav>
      </aside>

      <section className="main">
        <header className="topbar">
          <div className="title">
            <h1>Операционный экран</h1>
            <p>Очередь, столы, брони и остатки по филиалу в одном рабочем окне.</p>
          </div>
          <div className="toolbar">
            <select className="select" value={branch} onChange={(event) => setBranch(event.target.value)} aria-label="Филиал">
              <option value="center">Hookah Place Center</option>
              <option value="north">Hookah Place North</option>
            </select>
            <label className="search">
              <Search size={16} />
              <input value={query} onChange={(event) => setQuery(event.target.value)} placeholder="Поиск заказа" />
            </label>
            <button className="primary"><Plus size={18} />Новый заказ</button>
          </div>
        </header>

        <div className="dashboard">
          <section className="section">
            <div className="section-head">
              <h2>Очередь кальянов</h2>
              <span className="meta">{filteredOrders.length} активных</span>
            </div>
            <div className="queue">
              {filteredOrders.map((order) => (
                <article className="order-row" key={order.id}>
                  <div className="time">{order.time}</div>
                  <div>
                    <strong>{order.id} · {order.table}</strong>
                    <div className="meta">{order.mix} · кальянщик {order.master}</div>
                  </div>
                  <span className={`status ${order.status === "READY" ? "ok" : "warn"}`}>{order.status}</span>
                </article>
              ))}
            </div>

            <div className="section-head" style={{ marginTop: 18 }}>
              <h2>Схема зала</h2>
              <span className="meta">Main hall</span>
            </div>
            <div className="floor">
              {floorTables.map((table) => (
                <div
                  className={`table-dot ${table.status === "busy" ? "busy" : ""}`}
                  style={{ left: `${table.x}%`, top: `${table.y}%` }}
                  key={table.name}
                >
                  <div>
                    <strong>{table.name}</strong>
                    <div className="meta">{table.capacity} места</div>
                  </div>
                </div>
              ))}
            </div>
          </section>

          <aside className="right-stack">
            <section className="section">
              <div className="section-head">
                <h2>Ближайшие брони</h2>
                <CalendarClock size={18} />
              </div>
              <div className="queue">
                {bookingRows.map((booking) => (
                  <article className="booking-row" key={`${booking.time}-${booking.guest}`}>
                    <div>
                      <strong>{booking.time} · {booking.guest}</strong>
                      <div className="meta">{booking.table}</div>
                    </div>
                    <span className={`status ${booking.status === "CONFIRMED" ? "ok" : "warn"}`}>{booking.status}</span>
                  </article>
                ))}
              </div>
            </section>

            <div className="split">
              <section className="section">
                <div className="section-head">
                  <h2>Остатки</h2>
                </div>
                <div className="queue">
                  {stockRows.map((item) => (
                    <article className="stock-row" key={item.name}>
                      <div>
                        <strong>{item.name}</strong>
                        <div className="bar"><span style={{ width: `${Math.min(100, (item.grams / 160) * 100)}%` }} /></div>
                      </div>
                      <span className={`status ${item.grams < item.min ? "warn" : "ok"}`}>{item.grams} г</span>
                    </article>
                  ))}
                </div>
              </section>

              <section className="section">
                <div className="section-head">
                  <h2>Миксы</h2>
                </div>
                <div className="queue">
                  {mixes.map((mix) => (
                    <article className="mix-row" key={mix.name}>
                      <div>
                        <strong>{mix.name}</strong>
                        <div className="meta">{mix.strength}</div>
                      </div>
                      <span className="status ok">{mix.margin} ₽</span>
                    </article>
                  ))}
                </div>
              </section>
            </div>
          </aside>
        </div>
      </section>
    </main>
  );
}

type FloorPlan = {
  tables: Array<{
    name: string;
    status: string;
    capacity: number;
    xPosition: number;
    yPosition: number;
  }>;
};

type InventoryItem = {
  tobaccoId: string;
  stockGrams: number;
  minStockGrams: number;
};

type Booking = {
  clientId: string;
  tableId: string;
  startTime: string;
  status: string;
};
