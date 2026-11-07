package cn.tyw.launcher.ext;

import java.util.ArrayList;
import java.util.LinkedHashMap;
import java.util.List;
import java.util.Map;

/**
 * 极简 JSON 读写：让 Java 扩展不依赖任何第三方库即可使用 TML-Ext/1 协议。
 * 只处理 object / array / string / number / boolean / null。
 */
public final class Json {
    private Json() {}

    public static Object parse(String text) {
        if (text == null) return null;
        Parser p = new Parser(text);
        Object value = p.readValue();
        p.skipWs();
        return value;
    }

    @SuppressWarnings("unchecked")
    public static Map<String, Object> asMap(Object value) {
        return value instanceof Map ? (Map<String, Object>) value : new LinkedHashMap<String, Object>();
    }

    @SuppressWarnings("unchecked")
    public static List<Object> asList(Object value) {
        return value instanceof List ? (List<Object>) value : new ArrayList<Object>();
    }

    public static String str(Object value) {
        return value == null ? "" : String.valueOf(value);
    }

    public static boolean bool(Object value) {
        return value instanceof Boolean && (Boolean) value;
    }

    public static String stringify(Object value) {
        StringBuilder sb = new StringBuilder();
        write(sb, value);
        return sb.toString();
    }

    private static void write(StringBuilder sb, Object value) {
        if (value == null) {
            sb.append("null");
        } else if (value instanceof String) {
            writeString(sb, (String) value);
        } else if (value instanceof Boolean || value instanceof Number) {
            sb.append(value);
        } else if (value instanceof Map) {
            sb.append('{');
            boolean first = true;
            for (Map.Entry<?, ?> e : ((Map<?, ?>) value).entrySet()) {
                if (!first) sb.append(',');
                first = false;
                writeString(sb, String.valueOf(e.getKey()));
                sb.append(':');
                write(sb, e.getValue());
            }
            sb.append('}');
        } else if (value instanceof Iterable) {
            sb.append('[');
            boolean first = true;
            for (Object item : (Iterable<?>) value) {
                if (!first) sb.append(',');
                first = false;
                write(sb, item);
            }
            sb.append(']');
        } else {
            writeString(sb, String.valueOf(value));
        }
    }

    private static void writeString(StringBuilder sb, String s) {
        sb.append('"');
        for (int i = 0; i < s.length(); i++) {
            char c = s.charAt(i);
            switch (c) {
                case '"': sb.append("\\\""); break;
                case '\\': sb.append("\\\\"); break;
                case '\n': sb.append("\\n"); break;
                case '\r': sb.append("\\r"); break;
                case '\t': sb.append("\\t"); break;
                case '\b': sb.append("\\b"); break;
                case '\f': sb.append("\\f"); break;
                default:
                    if (c < 0x20) sb.append(String.format("\\u%04x", (int) c));
                    else sb.append(c);
            }
        }
        sb.append('"');
    }

    private static final class Parser {
        private final String text;
        private int pos;

        Parser(String text) { this.text = text; }

        void skipWs() {
            while (pos < text.length()) {
                char c = text.charAt(pos);
                if (c == ' ' || c == '\t' || c == '\r' || c == '\n') pos++;
                else break;
            }
        }

        Object readValue() {
            skipWs();
            if (pos >= text.length()) return null;
            char c = text.charAt(pos);
            switch (c) {
                case '{': return readObject();
                case '[': return readArray();
                case '"': return readString();
                case 't': pos += 4; return Boolean.TRUE;
                case 'f': pos += 5; return Boolean.FALSE;
                case 'n': pos += 4; return null;
                default: return readNumber();
            }
        }

        private Map<String, Object> readObject() {
            Map<String, Object> map = new LinkedHashMap<String, Object>();
            pos++; // {
            while (pos < text.length()) {
                skipWs();
                if (text.charAt(pos) == '}') { pos++; break; }
                if (text.charAt(pos) == ',') { pos++; continue; }
                String key = readString();
                skipWs();
                if (pos < text.length() && text.charAt(pos) == ':') pos++;
                map.put(key, readValue());
            }
            return map;
        }

        private List<Object> readArray() {
            List<Object> list = new ArrayList<Object>();
            pos++; // [
            while (pos < text.length()) {
                skipWs();
                if (text.charAt(pos) == ']') { pos++; break; }
                if (text.charAt(pos) == ',') { pos++; continue; }
                list.add(readValue());
            }
            return list;
        }

        private String readString() {
            skipWs();
            if (pos >= text.length() || text.charAt(pos) != '"') return "";
            pos++;
            StringBuilder sb = new StringBuilder();
            while (pos < text.length()) {
                char c = text.charAt(pos++);
                if (c == '"') break;
                if (c != '\\') { sb.append(c); continue; }
                if (pos >= text.length()) break;
                char e = text.charAt(pos++);
                switch (e) {
                    case 'n': sb.append('\n'); break;
                    case 'r': sb.append('\r'); break;
                    case 't': sb.append('\t'); break;
                    case 'b': sb.append('\b'); break;
                    case 'f': sb.append('\f'); break;
                    case 'u':
                        if (pos + 4 <= text.length()) {
                            try {
                                sb.append((char) Integer.parseInt(text.substring(pos, pos + 4), 16));
                            } catch (NumberFormatException ignored) {}
                            pos += 4;
                        }
                        break;
                    default: sb.append(e);
                }
            }
            return sb.toString();
        }

        private Object readNumber() {
            int start = pos;
            while (pos < text.length() && "-+.eE0123456789".indexOf(text.charAt(pos)) >= 0) pos++;
            if (start == pos) { pos++; return null; }
            String raw = text.substring(start, pos);
            try {
                if (raw.indexOf('.') < 0 && raw.indexOf('e') < 0 && raw.indexOf('E') < 0) {
                    return Long.valueOf(raw);
                }
                return Double.valueOf(raw);
            } catch (NumberFormatException ex) {
                return 0L;
            }
        }
    }
}
