// src/Login.tsx
import React, { useState, useEffect } from 'react';
import api from './api';
import './Login.css';

interface LoginProps {
  onLoginSuccess: () => void;
}

export default function Login({ onLoginSuccess }: LoginProps) {
  const [username, setUsername] = useState('');
  const [password, setPassword] = useState('');
  const [error, setError] = useState('');
  const [isLoading, setIsLoading] = useState(false);

  useEffect(() => {
    const hash = window.location.hash;
    if (!hash || !hash.includes('auth=')) return;

    const params = new URLSearchParams(hash.substring(1));
    const token = params.get('auth');

    if (token) {
      try {
        const payload = JSON.parse(atob(token.split('.')[1]));
        const role = payload['http://schemas.microsoft.com/ws/2008/06/identity/claims/role']
          || payload['role'] || '';
        const user = payload['http://schemas.xmlsoap.org/ws/2005/05/identity/claims/name']
          || payload['unique_name'] || '';

        const allowedRoles = ['Admin', 'Senior', 'Super'];
        if (allowedRoles.includes(role)) {
          localStorage.setItem('authToken', token);
          localStorage.setItem('userRole', role);
          if (user) localStorage.setItem('userName', user);
          window.history.replaceState(null, '', window.location.pathname);
          onLoginSuccess();
        }
      } catch {
        // malformed token — ignore
      }
    }
  }, [onLoginSuccess]);

  // Владелец клуба и админ платформы входят по email из кабинета:
  // внутри своего клуба такой аккаунт получает права Super.
  const tryAccountLogin = async (): Promise<boolean> => {
    try {
      const res = await api.post('/account/login', {
        email: username,
        password: password
      });
      const role = res.data.role;
      if (role !== 'PlatformAdmin' && role !== 'Owner') return false;

      localStorage.setItem('authToken', res.data.token);
      localStorage.setItem('userRole', 'Super');
      localStorage.setItem(
        'userName',
        res.data.displayName || res.data.email || username
      );

      // Тот же токен открывает и кабинет, поэтому заводим его сессию сразу:
      // иначе кнопка «В кабинет» привела бы на повторный вход тем же паролем.
      localStorage.setItem('cabinet_token', res.data.token);
      localStorage.setItem('cabinet_user', JSON.stringify({
        username: res.data.displayName || res.data.email,
        email: res.data.email,
        role
      }));

      onLoginSuccess();
      return true;
    } catch {
      return false;
    }
  };

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault();
    setError('');
    setIsLoading(true);

    try {
      const response = await api.post('/Auth/login', {
        username: username,
        password: password
      });

      if (response.status === 200) {
        const userRole = response.data.role || 'Client';
        
        const allowedRoles = ['Admin', 'Senior', 'Super'];
      
        if (!allowedRoles.includes(userRole)) {
          setError('Доступ запрещен. Это панель для администраторов.');
          setIsLoading(false);
          return;
        }
      
        localStorage.setItem('authToken', response.data.token);
        localStorage.setItem('userRole', userRole);
        if (response.data.username) {
          localStorage.setItem('userName', response.data.username);
        } else {
          localStorage.setItem('userName', username);
        }
        onLoginSuccess();
      }
    } catch (err: any) {
      // Персонал не нашёлся — пробуем как аккаунт кабинета (email + пароль).
      if (err.response && await tryAccountLogin()) return;

      console.error('Login error:', err);
      if (err.response) {
        const data = err.response.data;
        setError((typeof data === 'string' ? data : data?.error) || 'Неверный логин или пароль');
      } else if (err.request) {
        setError('Сервер недоступен. Проверьте подключение.');
      } else {
        setError('Ошибка: ' + err.message);
      }
    } finally {
        setIsLoading(false);
    }
  };

  return (
    <div className="login-container">
      <div className="login-card">
        <div className="brand-section">
            <img src={`${import.meta.env.BASE_URL}images/logo.png`} alt="Aether" className="brand-logo" />
            <h1>Aether</h1>
            <small>Панель управления клубом</small>
        </div>

        {error && <div className="flash-error">{error}</div>}

        <form onSubmit={handleSubmit}>
          <div className="form-group">
            <label htmlFor="username">Пользователь</label>
            <input 
              type="text" 
              id="username"
              placeholder="Введите логин"
              value={username}
              onChange={(e) => setUsername(e.target.value)}
              required 
              autoFocus
            />
          </div>
          
          <div className="form-group">
            <label htmlFor="password">Пароль</label>
            <input 
              type="password" 
              id="password" 
              placeholder="••••••••"
              value={password}
              onChange={(e) => setPassword(e.target.value)}
              required 
            />
          </div>
          
          <button type="submit" className="btn-submit" disabled={isLoading}>
            {isLoading ? 'Вход...' : 'Войти в систему'}
          </button>
        </form>
      </div>
    </div>
  );
}
