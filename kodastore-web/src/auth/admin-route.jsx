import { Navigate } from "react-router";
import UseAuth from "../hooks/use-auth";

export const AdminRoute = ({ children }) => {
  const { user, isAuthenticated } = UseAuth();
  console.log("ADMIN BUKAN : ", { isAuthenticated, user });

  if (!isAuthenticated) {
    return <Navigate to="/login" replace />;
  }
  const isAdmin = user?.roles?.includes("ADMIN") ?? false;

  if (!isAdmin) {
    return <Navigate to="/dashboard" replace />;
  }
  return children;
};
