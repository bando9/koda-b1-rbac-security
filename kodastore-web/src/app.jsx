import { Navigate, Route, Routes } from "react-router";
import LoginPage from "./pages/login-page";
import ProtectedRoute from "./auth/protected-route";
import DashboardPage from "./pages/dashboard-page";
import { AdminRoute } from "./auth/admin-route";
import { AdminCategoryPage } from "./pages/admin-category-page";

function App() {
  return (
    <Routes>
      <Route path="/login" element={<LoginPage />} />
      <Route
        path="/dashboard"
        element={
          <ProtectedRoute>
            <DashboardPage />
          </ProtectedRoute>
        }
      />

      <Route
        path="/admin/category"
        element={
          <ProtectedRoute>
            <AdminRoute>
              <AdminCategoryPage />
            </AdminRoute>
          </ProtectedRoute>
        }
      />

      <Route path="*" element={<Navigate to="/dashboard" />} replace />
    </Routes>
  );
}

export default App;
