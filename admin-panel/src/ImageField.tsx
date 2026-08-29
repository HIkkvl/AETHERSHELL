import { useRef, useState } from "react";
import { uploadImage } from "./api";
import "./ImageField.css";

interface Props {
    label?: string;
    value: string;
    onChange: (url: string) => void;
    placeholder?: string;
    required?: boolean;
}

/// Поле картинки: можно вставить ссылку, выбрать файл или перетащить его сюда.
/// Загруженный файл сервер отдаёт с того же origin, поэтому в шелле он тоже виден.
export default function ImageField({ label = 'Картинка', value, onChange, placeholder = 'https://...', required }: Props) {
    const inputRef = useRef<HTMLInputElement>(null);
    const [uploading, setUploading] = useState(false);
    const [error, setError] = useState<string | null>(null);
    const [dragging, setDragging] = useState(false);

    const upload = async (file: File) => {
        setError(null);
        setUploading(true);
        try {
            const url = await uploadImage(file);
            onChange(url);
        } catch (e: any) {
            setError(e?.response?.data?.error || 'Не удалось загрузить файл');
        } finally {
            setUploading(false);
        }
    };

    const handleDrop = (e: React.DragEvent) => {
        e.preventDefault();
        setDragging(false);
        const file = e.dataTransfer.files?.[0];
        if (file) upload(file);
    };

    return (
        <div className="form-group image-field">
            <label>{label}</label>

            <input
                className="form-input"
                value={value}
                onChange={(e) => onChange(e.target.value)}
                placeholder={placeholder}
                required={required}
            />

            <div
                className={`image-drop ${dragging ? 'dragging' : ''}`}
                onDragOver={(e) => { e.preventDefault(); setDragging(true); }}
                onDragLeave={() => setDragging(false)}
                onDrop={handleDrop}
                onClick={() => inputRef.current?.click()}
            >
                {uploading
                    ? 'Загрузка...'
                    : 'Загрузить с компьютера — нажмите или перетащите файл'}
            </div>

            <input
                ref={inputRef}
                type="file"
                accept="image/jpeg,image/png,image/webp,image/gif"
                style={{ display: 'none' }}
                onChange={(e) => {
                    const file = e.target.files?.[0];
                    if (file) upload(file);
                    e.target.value = '';
                }}
            />

            {error && <div className="image-error">{error}</div>}

            {value && (
                <div className="image-preview">
                    <img src={value} alt="Превью" onError={(e) => (e.currentTarget.style.display = 'none')} />
                </div>
            )}
        </div>
    );
}
