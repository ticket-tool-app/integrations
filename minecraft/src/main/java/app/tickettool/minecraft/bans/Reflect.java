package app.tickettool.minecraft.bans;

import java.lang.reflect.InvocationTargetException;
import java.lang.reflect.Method;
import java.lang.reflect.Modifier;
import java.util.ArrayDeque;
import java.util.Deque;
import java.util.HashSet;
import java.util.Set;

/**
 * Calls into ban plugins by name, so the jar needs none of their APIs to build
 * or load and works with whichever release the server runs.
 */
final class Reflect {
    private Reflect() {}

    static Class<?> type(ClassLoader loader, String name) throws ClassNotFoundException {
        return Class.forName(name, true, loader);
    }

    static Object callStatic(Class<?> type, String name, Object... args) throws ReflectiveOperationException {
        return invoke(find(type, name, args, true), null, args);
    }

    static Object call(Object target, String name, Object... args) throws ReflectiveOperationException {
        if (target == null) {
            throw new NoSuchMethodException(name + " on null");
        }
        return invoke(find(target.getClass(), name, args, false), target, args);
    }

    static boolean has(Object target, String name, int arity) {
        if (target == null) {
            return false;
        }
        for (Method m : target.getClass().getMethods()) {
            if (m.getName().equals(name) && m.getParameterCount() == arity) {
                return true;
            }
        }
        return false;
    }

    private static Object invoke(Method m, Object target, Object[] args) throws ReflectiveOperationException {
        try {
            return m.invoke(target, args);
        } catch (InvocationTargetException e) {
            if (e.getCause() instanceof Exception cause) {
                throw new ReflectiveOperationException(cause.getMessage(), cause);
            }
            throw e;
        }
    }

    /**
     * A public method declared on a public type. Implementation classes are
     * often package-private, and invoking through their Method object fails
     * even when the method itself is public, so look up the interface's.
     */
    private static Method find(Class<?> start, String name, Object[] args, boolean wantStatic)
            throws NoSuchMethodException {
        Deque<Class<?>> queue = new ArrayDeque<>();
        Set<Class<?>> seen = new HashSet<>();
        queue.add(start);
        while (!queue.isEmpty()) {
            Class<?> c = queue.poll();
            if (!seen.add(c)) {
                continue;
            }
            if (Modifier.isPublic(c.getModifiers())) {
                for (Method m : c.getDeclaredMethods()) {
                    if (m.getName().equals(name) && Modifier.isPublic(m.getModifiers())
                            && Modifier.isStatic(m.getModifiers()) == wantStatic && accepts(m, args)) {
                        return m;
                    }
                }
            }
            if (c.getSuperclass() != null) {
                queue.add(c.getSuperclass());
            }
            queue.addAll(java.util.List.of(c.getInterfaces()));
        }
        throw new NoSuchMethodException(start.getName() + "." + name + " with " + args.length + " arguments");
    }

    private static boolean accepts(Method m, Object[] args) {
        Class<?>[] params = m.getParameterTypes();
        if (params.length != args.length) {
            return false;
        }
        for (int i = 0; i < params.length; i++) {
            if (args[i] == null) {
                if (params[i].isPrimitive()) {
                    return false;
                }
            } else if (!box(params[i]).isInstance(args[i])) {
                return false;
            }
        }
        return true;
    }

    private static Class<?> box(Class<?> c) {
        if (!c.isPrimitive()) {
            return c;
        }
        if (c == long.class) {
            return Long.class;
        }
        if (c == int.class) {
            return Integer.class;
        }
        if (c == boolean.class) {
            return Boolean.class;
        }
        if (c == double.class) {
            return Double.class;
        }
        return Object.class;
    }
}
