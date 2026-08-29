import { useEffect, useState, useRef, useMemo } from "react";
import { getComputers, getChatHistory, clearChatHistory, type Computer } from "./api";
import * as signalR from "@microsoft/signalr";
import "./Chat.css";

import { getHubUrl } from './api';

// Оповещение других компонентов (меню) о непрочитанных
const notifyStorageChange = () => {
    window.dispatchEvent(new Event("club_storage_update"));
};

interface ChatMessage {
  id: number;
  message: string;
  isFromAdmin: boolean;
  createdAt: string;
}

export default function Chat() {
  const [computers, setComputers] = useState<Computer[]>([]);
  const [selectedPc, setSelectedPc] = useState<Computer | null>(null);
  const [messages, setMessages] = useState<ChatMessage[]>([]);
  const [input, setInput] = useState("");

  // Загрузка непрочитанных из LocalStorage
  const loadUnread = () => {
    try {
      const saved = localStorage.getItem("chat_unread_store");
      return saved ? JSON.parse(saved) : {};
    } catch { return {}; }
  };

  const [unread, setUnread] = useState<Record<string, number>>(loadUnread);

  // Refs для доступа внутри замыканий SignalR
  const selectedPcRef = useRef<Computer | null>(null);
  const connectionRef = useRef<signalR.HubConnection | null>(null);
  const messagesEndRef = useRef<HTMLDivElement>(null);

  // Обновляем ref при смене выбранного ПК
  useEffect(() => { selectedPcRef.current = selectedPc; }, [selectedPc]);

  // Синхронизация бейджей (между вкладками/компонентами)
  useEffect(() => {
    const handleUpdate = () => setUnread(loadUnread());
    window.addEventListener("club_storage_update", handleUpdate);
    window.addEventListener("storage", handleUpdate);
    return () => {
        window.removeEventListener("club_storage_update", handleUpdate);
        window.removeEventListener("storage", handleUpdate);
    };
  }, []);

  // Автоскролл вниз
  useEffect(() => {
    messagesEndRef.current?.scrollIntoView({ behavior: "smooth" });
  }, [messages]);

  // ================= SIGNALR (ИСПРАВЛЕНО) =================
  useEffect(() => {
    const token = localStorage.getItem('authToken');
    if (!token) return;

    const conn = new signalR.HubConnectionBuilder()
      .withUrl(getHubUrl(), {
          accessTokenFactory: () => token,
          skipNegotiation: true,
          transport: signalR.HttpTransportType.WebSockets
      })
      .withAutomaticReconnect()
      .configureLogging(signalR.LogLevel.Information) // Включаем логи SignalR
      .build();

    // --- ЕДИНАЯ ЛОГИКА ПРИЕМА СООБЩЕНИЙ ---
    const handleIncomingMessage = (pcName: string, message: string) => {
        console.log("📩 Incoming MSG:", pcName, message); // Лог для отладки

        const incomingName = pcName.toLowerCase();
        
        // 1. Если этот ПК сейчас открыт в чате
        if (selectedPcRef.current?.pcName.toLowerCase() === incomingName) {
           setMessages(prev => [...prev, {
             id: Date.now(),
             message: message,
             isFromAdmin: false, // Это пришло от клиента
             createdAt: new Date().toISOString()
           }]);
        } 
        // 2. Если чат с этим ПК закрыт (или открыт другой)
        else {
           const currentStore = loadUnread();
           currentStore[incomingName] = (currentStore[incomingName] || 0) + 1;
           
           localStorage.setItem("chat_unread_store", JSON.stringify(currentStore));
           setUnread(currentStore);
           notifyStorageChange();

           // Звук
           try {
               const audio = new Audio("/notification.mp3");
               audio.play().catch(e => console.warn("Sound blocked", e));
           } catch (e) {}
        }
    };

    conn.start().then(() => {
      console.log("🟢 Chat Connected (Admin)");
      
      // Обязательно вступаем в группу админов
      conn.invoke("JoinAdminGroup").catch(err => console.error("JoinAdminGroup failed:", err));

      // --- ПОДПИСЫВАЕМСЯ НА ВСЕ ВОЗМОЖНЫЕ ИМЕНА СОБЫТИЙ ---
      
      // Вариант 1: Специальное событие для админов (если сервер так настроен)
      conn.on("ReceiveMessageFromClient", handleIncomingMessage);
      
      // Вариант 2: Стандартное событие (то же, что слушает клиент)
      conn.on("ReceiveMessage", (sender: string, message: string) => {
          // Если сервер шлет (User, Message, IsAdmin), то аргументы могут отличаться.
          // Обычно ReceiveMessage(user, message).
          // Проверяем, не от нас ли это (хотя в админке sender - это имя ПК)
          handleIncomingMessage(sender, message);
      });

      // Очистка чата
      conn.on("ChatCleared", (clearedPcName: string) => {
          if (selectedPcRef.current?.pcName === clearedPcName) setMessages([]);
          
          const currentStore = loadUnread();
          if (currentStore[clearedPcName.toLowerCase()]) {
              delete currentStore[clearedPcName.toLowerCase()];
              localStorage.setItem("chat_unread_store", JSON.stringify(currentStore));
              setUnread(currentStore);
              notifyStorageChange();
          }
      });

    }).catch(err => console.error("SignalR Connection Error:", err));

    connectionRef.current = conn;

    return () => {
        conn.stop();
    };
  }, []);

  // Периодическое обновление списка ПК
  useEffect(() => {
    const fetchPC = () => getComputers().then(setComputers).catch(console.error);
    fetchPC();
    const interval = setInterval(fetchPC, 5000);
    return () => clearInterval(interval);
  }, []);

  // Клик по ПК в списке
  const handlePcClick = async (pc: Computer) => {
    setSelectedPc(pc);
    
    // Снимаем бейдж уведомления
    const currentStore = loadUnread();
    const key = pc.pcName.toLowerCase();
    
    if (currentStore[key]) {
        delete currentStore[key];
        localStorage.setItem("chat_unread_store", JSON.stringify(currentStore));
        setUnread(currentStore);
        notifyStorageChange();
    }
  };

  // Загрузка истории при выборе ПК
  useEffect(() => {
    if (!selectedPc) return;
    setMessages([]); 
    getChatHistory(selectedPc.pcName).then(setMessages).catch(console.error);
  }, [selectedPc]);

  // Отправка сообщения
  const handleSend = async () => {
    if (!selectedPc || !input.trim() || !connectionRef.current) return;
    try {
      await connectionRef.current.invoke("SendToPc", selectedPc.pcName, input);
      
      // Добавляем свое сообщение сразу
      setMessages(prev => [...prev, { 
          id: Date.now(), 
          message: input, 
          isFromAdmin: true, 
          createdAt: new Date().toISOString() 
      }]);
      
      setInput("");
    } catch (e) { console.error("Send Error:", e); }
  };

  const handleKeyDown = (e: React.KeyboardEvent<HTMLInputElement>) => {
      if (e.key === 'Enter') {
          e.preventDefault();
          handleSend();
      }
  };

  const handleClearClick = async () => {
    if (!selectedPc) return;
    if (window.confirm(`Очистить историю чата с ${selectedPc.nameToDisplay}?`)) {
        await clearChatHistory(selectedPc.pcName);
        setMessages([]);
    }
  };

  // Сортировка: Сначала с непрочитанными, потом по ID
  const sortedComputers = useMemo(() => {
      return [...computers].sort((a, b) => {
          const countA = unread[a.pcName.toLowerCase()] || 0;
          const countB = unread[b.pcName.toLowerCase()] || 0;
          if (countA > 0 && countB === 0) return -1;
          if (countA === 0 && countB > 0) return 1;
          return a.id - b.id;
      });
  }, [computers, unread]);

  const formatTime = (dateStr: string) => {
      try { return new Date(dateStr).toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' }); } 
      catch { return ""; }
  };

  return (
    <div className="chat-page">
      <div className="chat-container">
        <div className="pc-list-sidebar">
          <div className="list-header"><h3>Активные сессии</h3></div>
          <div className="list-scroll">
            {sortedComputers.map(pc => {
              const count = unread[pc.pcName.toLowerCase()] || 0;
              const isSelected = selectedPc?.id === pc.id;
              return (
                <div key={pc.id} className={`pc-item ${isSelected ? 'active' : ''}`} onClick={() => handlePcClick(pc)}>
                  <div className="pc-avatar">
                    PC
                    <span className={`status-dot ${pc.isOnline ? 'online' : 'offline'}`} />
                  </div>
                  <div className="pc-info">
                    <div className="pc-name">{pc.nameToDisplay}</div>
                    <div className="pc-group">{pc.groupName}</div>
                  </div>
                  {count > 0 && <div className="unread-badge">{count}</div>}
                </div>
              );
            })}
          </div>
        </div>

        <div className="chat-main-area">
          {selectedPc ? (
            <>
              <div className="chat-main-header">
                <div className="header-info">
                  <b>{selectedPc.nameToDisplay}</b>
                  <span className="status-text">{selectedPc.isOnline ? 'В сети' : 'Не в сети'}</span>
                </div>
                <button type="button" onClick={handleClearClick} className="clear-btn">Очистить</button>
              </div>

              <div className="messages-box">
                {messages.map((m, i) => (
                  <div key={m.id || i} className={`msg-row ${m.isFromAdmin ? 'admin-row' : 'client-row'}`}>
                    <div className={`msg-bubble ${m.isFromAdmin ? 'admin-bubble' : 'client-bubble'}`}>
                      <div className="msg-text">{m.message}</div>
                      <div className="msg-time">{formatTime(m.createdAt)}</div>
                    </div>
                  </div>
                ))}
                <div ref={messagesEndRef} />
              </div>

              <div className="chat-input-area">
                <input
                  value={input}
                  onChange={e => setInput(e.target.value)}
                  onKeyDown={handleKeyDown}
                  placeholder="Написать сообщение игроку..."
                  autoFocus
                />
                <button type="button" onClick={handleSend} disabled={!input.trim()}>→</button>
              </div>
            </>
          ) : (
            <div className="no-chat-selected">
              <h3>Выберите компьютер</h3>
              <p>Нажмите на ПК слева, чтобы начать чат</p>
            </div>
          )}
        </div>
      </div>
    </div>
  );
}